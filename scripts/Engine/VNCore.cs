using Godot;
using System;
using System.Collections.Generic;

namespace 交互式文本.Engine
{
    /// <summary>
    /// 视觉小说主控制器。挂载在主场景中，负责剧情流程、输入、状态管理。
    /// </summary>
    public partial class VNCore : Node
    {
        [Export] public string StartSceneId = "start";
        [Export] public string StoryFile = "res://story/main.json";

        public ScriptParser Parser { get; private set; } = new();
        public GameState State { get; private set; } = new();
        public CommandRunner Runner { get; private set; }

        public bool IsWaitingForInput { get; set; } = false;
        public bool IsTyping { get; set; } = false;
        public bool IsAutoMode { get; set; } = false;
        public bool IsSkipAllMode { get; set; } = false;
        public bool IsSkipReadMode { get; set; } = false;
        public bool IsTransitioning { get; set; } = false;

        // 节点引用
        public DialogueBox DialogueBox { get; private set; }
        public Node2D CharacterStage { get; private set; }
        public TextureRect Background { get; private set; }
        public CanvasLayer TransitionLayer { get; private set; }

        /// <summary>立绘高度占屏幕高度的比例（常见视觉小说约为 0.85~0.95）。</summary>
        public const float CharacterHeightRatio = 0.9f;

        private bool _advanceRequested = false;
        private double _autoTimer = 0.0;
        private double _skipTimer = 0.0;

        public override void _Ready()
        {
            RegisterInputActions();

            Runner = new CommandRunner(this);

            DialogueBox = GetNode<DialogueBox>("UI/DialogueBox");
            CharacterStage = GetNode<Node2D>("Stage/Characters");
            Background = GetNode<TextureRect>("Stage/Background");

            Error err = Parser.LoadFromJson(StoryFile);
            if (err != Error.Ok)
            {
                GD.PushError($"剧情加载失败，无法继续。错误码: {err}");
                return;
            }

            State.CurrentSceneId = StartSceneId;
            State.CurrentCommandIndex = 0;

            ApplyVolumes();
            RunNextCommand();
        }

        public override void _Process(double delta)
        {
            if (IsAutoMode && IsWaitingForInput && !IsTyping && !IsBlockingVoice() && !IsTransitioning)
            {
                _autoTimer += delta;
                if (_autoTimer >= Data.GameConfig.Load().AutoDelay)
                {
                    _autoTimer = 0.0;
                    Advance();
                }
            }

            if ((IsSkipAllMode || IsSkipReadMode) && IsWaitingForInput && !IsTyping && !IsBlockingVoice() && !IsTransitioning)
            {
                _skipTimer += delta;
                if (_skipTimer >= Data.GameConfig.Load().SkipDelay)
                {
                    _skipTimer = 0.0;
                    Advance();
                }
            }

            if (_advanceRequested && !IsTransitioning)
            {
                _advanceRequested = false;
                _autoTimer = 0.0;
                RunNextCommand();
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            // 任意 UI 面板打开时，阻断剧情推进输入
            if (UIManager.Instance != null && UIManager.Instance.IsAnyUIOpen)
                return;

            if (@event.IsActionPressed("vn_advance"))
            {
                if (IsTyping)
                {
                    DialogueBox?.SkipTyping();
                    GetViewport().SetInputAsHandled();
                }
                else if (IsWaitingForInput)
                {
                    Advance();
                    GetViewport().SetInputAsHandled();
                }
            }

            if (@event.IsActionPressed("vn_history"))
            {
                UIManager.Instance?.ShowHistory();
                GetViewport().SetInputAsHandled();
            }

            if (@event.IsActionPressed("vn_auto"))
            {
                ToggleAutoMode();
                GetViewport().SetInputAsHandled();
            }

            if (@event.IsActionPressed("vn_skip"))
            {
                IsSkipReadMode = true;
                GetViewport().SetInputAsHandled();
            }
            else if (@event.IsActionReleased("vn_skip"))
            {
                IsSkipReadMode = false;
            }
        }

        public void Advance()
        {
            if (IsWaitingForInput)
            {
                IsWaitingForInput = false;
                State.CurrentCommandIndex++;
                _advanceRequested = true;
            }
        }

        public void RunNextCommand()
        {
            if (!Parser.Scenes.TryGetValue(State.CurrentSceneId, out var scene))
            {
                GD.PushError($"未找到场景: {State.CurrentSceneId}");
                return;
            }

            if (State.CurrentCommandIndex >= scene.Commands.Count)
            {
                GD.Print($"场景 {State.CurrentSceneId} 结束。");
                return;
            }

            var command = scene.Commands[State.CurrentCommandIndex];

            // SKIP 已读模式：跳过非阻塞的已读命令
            if (IsSkipReadMode && ShouldSkipReadCommand(command))
            {
                State.MarkRead(State.CurrentSceneId, State.CurrentCommandIndex);
                State.CurrentCommandIndex++;
                CallDeferred(nameof(RunNextCommand));
                return;
            }

            State.MarkRead(State.CurrentSceneId, State.CurrentCommandIndex);
            IsWaitingForInput = false;
            Runner.Execute(command);

            if (!IsWaitingForInput && !IsTyping && !IsTransitioning)
            {
                State.CurrentCommandIndex++;
                CallDeferred(nameof(RunNextCommand));
            }
        }

        public void OnDialogueComplete()
        {
            IsTyping = false;
            IsWaitingForInput = true;
        }

        public void OnChoiceMade(string targetSceneId)
        {
            State.CurrentSceneId = targetSceneId;
            State.CurrentCommandIndex = 0;
            RunNextCommand();
        }

        public void JumpTo(string sceneId, int commandIndex = 0)
        {
            State.CurrentSceneId = sceneId;
            State.CurrentCommandIndex = commandIndex;
            RunNextCommand();
        }

        public void ToggleAutoMode()
        {
            IsAutoMode = !IsAutoMode;
            IsSkipReadMode = false;
            IsSkipAllMode = false;
        }

        public void ToggleSkipAllMode()
        {
            IsSkipAllMode = !IsSkipAllMode;
            IsAutoMode = false;
        }

        public void ApplySaveData(Data.SaveData data)
        {
            if (data == null) return;

            State.CurrentSceneId = data.CurrentSceneId;
            State.CurrentCommandIndex = data.CurrentCommandIndex;
            State.Variables = data.Variables ?? new();
            State.ReadFlags = data.ReadFlags ?? new();
            State.History = data.History ?? new();
            State.Stage = data.Stage ?? new();
            State.Audio = data.Audio ?? new();
            State.Unlocks = data.Unlocks ?? new();

            RestoreStage(State.Stage);
            RestoreAudio(State.Audio);

            DialogueBox?.ClearChoices();
            RunNextCommand();
        }

        private void RestoreStage(StageSnapshot stage)
        {
            if (!string.IsNullOrEmpty(stage.BackgroundPath))
            {
                var texture = GD.Load<Texture2D>($"res://assets/backgrounds/{stage.BackgroundPath}.png");
                if (texture != null)
                    Background.Texture = texture;
            }
            else
            {
                Background.Texture = null;
                Background.Modulate = new Color(stage.BackgroundColor);
            }

            foreach (var child in CharacterStage.GetChildren())
                child.QueueFree();

            foreach (var kv in stage.Characters)
            {
                var ch = kv.Value;
                if (!ch.Visible) continue;

                var sprite = new Sprite2D { Name = ch.CharacterId };
                CharacterStage.AddChild(sprite);
                var texture = GD.Load<Texture2D>($"res://assets/characters/{ch.CharacterId}/{ch.Emotion}.png");
                if (texture != null)
                {
                    sprite.Texture = texture;
                    ApplyCharacterScale(sprite);
                }
            }
        }

        private void RestoreAudio(AudioSnapshot audio)
        {
            if (!string.IsNullOrEmpty(audio.BgmTrack))
                Audio.AudioManager.Instance?.PlayBgm(audio.BgmTrack, 0f, audio.BgmLoop);
        }

        /// <summary>
        /// 根据屏幕高度统一缩放立绘，使其高度占屏幕高度的固定比例。
        /// </summary>
        public void ApplyCharacterScale(Sprite2D sprite)
        {
            if (sprite?.Texture == null) return;

            float viewportHeight = GetViewport().GetVisibleRect().Size.Y;
            float targetHeight = viewportHeight * CharacterHeightRatio;
            float scale = targetHeight / sprite.Texture.GetHeight();
            sprite.Scale = new Vector2(scale, scale);
        }

        /// <summary>
        /// 场景切换完成后的回调。清除过渡状态并推进到下一条命令。
        /// </summary>
        public void OnTransitionFinished()
        {
            IsTransitioning = false;
            _advanceRequested = false;
            State.CurrentCommandIndex++;
            RunNextCommand();
        }

        /// <summary>
        /// 黑屏淡入淡出切换背景。动画完成后调用 onFinished。
        /// </summary>
        public void TransitionToBackground(Texture2D texture, float duration, Action onFinished)
        {
            if (duration <= 0f)
            {
                Background.Texture = texture;
                Background.Modulate = Colors.White;
                onFinished?.Invoke();
                return;
            }

            var overlay = GetOrCreateTransitionOverlay();
            overlay.Color = Colors.Black;
            overlay.Modulate = new Color(1, 1, 1, 0);
            overlay.Visible = true;
            overlay.MouseFilter = Control.MouseFilterEnum.Ignore;

            float halfDuration = duration * 0.5f;

            var fadeIn = overlay.CreateTween();
            fadeIn.TweenProperty(overlay, "modulate:a", 1.0f, halfDuration);
            fadeIn.Finished += () =>
            {
                Background.Texture = texture;
                Background.Modulate = Colors.White;

                var fadeOut = overlay.CreateTween();
                fadeOut.TweenProperty(overlay, "modulate:a", 0.0f, halfDuration);
                fadeOut.Finished += () =>
                {
                    overlay.Visible = false;
                    onFinished?.Invoke();
                };
            };
        }

        private ColorRect GetOrCreateTransitionOverlay()
        {
            if (TransitionLayer == null)
            {
                TransitionLayer = new CanvasLayer { Name = "TransitionLayer", Layer = 10 };
                AddChild(TransitionLayer);
            }

            var overlay = TransitionLayer.GetNodeOrNull<ColorRect>("Overlay");
            if (overlay == null)
            {
                overlay = new ColorRect
                {
                    Name = "Overlay",
                    Color = Colors.Black,
                    AnchorsPreset = (int)Control.LayoutPreset.FullRect
                };
                TransitionLayer.AddChild(overlay);
            }
            return overlay;
        }

        private bool ShouldSkipReadCommand(CommandData command)
        {
            // 选择支和条件块不应跳过
            if (command.Cmd == "choice" || command.Cmd == "if" || command.Cmd == "jump")
                return false;

            return State.IsRead(State.CurrentSceneId, State.CurrentCommandIndex);
        }

        private bool IsBlockingVoice()
        {
            return Audio.AudioManager.Instance?.VoiceChannel?.IsPlaying ?? false;
        }

        private void ApplyVolumes()
        {
            Audio.AudioManager.Instance?.ApplyVolumes();
        }

        private void RegisterInputActions()
        {
            RegisterAction("vn_advance", Key.Space, Key.Enter, MouseButton.Left, MouseButton.WheelDown);
            RegisterKeyAction("vn_history", Key.H);
            RegisterKeyAction("vn_auto", Key.A);
            RegisterKeyAction("vn_skip", Key.Ctrl);
            RegisterKeyAction("vn_menu", Key.Escape);
        }

        private void RegisterAction(string action, Key key1, Key key2, MouseButton mouse, MouseButton wheel)
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);

            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key1 });
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key2 });
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = mouse });
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = wheel });
        }

        private void RegisterKeyAction(string action, Key key)
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}
