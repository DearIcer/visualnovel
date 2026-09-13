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
        [Export] public string StartSceneId = "pro0_monologue";
        [Export] public string StoryFile = "res://story/main.json";

        /// <summary>启动时是否先显示主菜单（由 Web UI 渲染标题界面），false 则直接进入剧情。</summary>
        [Export] public bool ShowMainMenu = true;

        /// <summary>是否已经开始游戏（主菜单点开始或读档后置为 true）。</summary>
        public bool GameStarted { get; private set; } = false;

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
        /// <summary>全屏 CG 展示层（覆盖背景与立绘，由 cgshow/cghide 指令驱动）。</summary>
        public TextureRect CgOverlay { get; private set; }
        public CanvasLayer TransitionLayer { get; private set; }

        /// <summary>立绘高度占屏幕高度的比例（常见视觉小说约为 0.85~0.95）。</summary>
        public const float CharacterHeightRatio = 0.9f;

        private bool _advanceRequested = false;
        private double _autoTimer = 0.0;
        private double _skipTimer = 0.0;
        private bool _redirectRequested = false;
        private bool _runScheduled = false;
        private bool _skipNextCommand = false;
        private Tween _backgroundTransitionTween;
        private int _transitionGeneration = 0;

        public override void _Ready()
        {
            RegisterInputActions();

            Runner = new CommandRunner(this);

            DialogueBox = GetNode<DialogueBox>("UI/DialogueBox");
            CharacterStage = GetNode<Node2D>("Stage/Characters");
            Background = GetNode<TextureRect>("Stage/Background");

            // 全屏 CG 层：挂在 Stage 最上层（盖住背景与立绘），初始隐藏
            CgOverlay = new TextureRect
            {
                Name = "CgOverlay",
                AnchorsPreset = (int)Control.LayoutPreset.FullRect,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = new Color(1, 1, 1, 0),
                Visible = false
            };
            GetNode<CanvasLayer>("Stage").AddChild(CgOverlay);

            Error err = Parser.LoadFromJson(StoryFile);
            if (err != Error.Ok)
            {
                GD.PushError($"剧情加载失败，无法继续。错误码: {err}");
                return;
            }

            ValidateStoryAssets();

            State.CurrentSceneId = StartSceneId;
            State.CurrentCommandIndex = 0;

            ApplyVolumes();
            WebUI.Instance?.SendStoryMeta();

            if (ShowMainMenu)
            {
                // 主菜单由 Web UI 渲染：等待 JS 的 menu_new_game / load_slot 消息再开始。
                GD.Print("主菜单待命中，等待玩家开始游戏。");
            }
            else
            {
                GameStarted = true;
                RunNextCommand();
            }
        }

        /// <summary>从主菜单开始新游戏。</summary>
        public void StartNewGame()
        {
            if (GameStarted) return;
            GameStarted = true;
            State.CurrentSceneId = StartSceneId;
            State.CurrentCommandIndex = 0;
            IsWaitingForInput = false;
            IsTyping = false;
            _advanceRequested = false;
            RunNextCommand();
        }

        /// <summary>读档开始时标记游戏已进入进行状态（供 WebUI 调用）。</summary>
        public void MarkGameStarted()
        {
            GameStarted = true;
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
            // 主菜单阶段不响应任何剧情输入
            if (!GameStarted)
                return;

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
            _runScheduled = false;
            _redirectRequested = false;

            if (!Parser.Scenes.TryGetValue(State.CurrentSceneId, out var scene))
            {
                GD.PushError($"未找到场景: {State.CurrentSceneId}");
                return;
            }

            if (State.CurrentCommandIndex >= scene.Commands.Count)
            {
                NotifyStoryProgress();
                GD.Print($"场景 {State.CurrentSceneId} 结束。");
                return;
            }

            var command = scene.Commands[State.CurrentCommandIndex];

            // SKIP 已读模式：只跳过已读的展示指令
            if (IsSkipReadMode && ShouldSkipReadCommand(command))
            {
                State.MarkRead(State.CurrentSceneId, State.CurrentCommandIndex);
                State.CurrentCommandIndex++;
                NotifyStoryProgress();
                ScheduleRunNextCommand();
                return;
            }

            State.MarkRead(State.CurrentSceneId, State.CurrentCommandIndex);
            IsWaitingForInput = false;
            Runner.Execute(command);
            NotifyStoryProgress();

            // jump 会在本次调用返回后重新调度目标场景，不能再递增目标索引。
            if (_redirectRequested)
                return;

            if (!IsWaitingForInput && !IsTyping && !IsTransitioning)
            {
                State.CurrentCommandIndex++;
                if (_skipNextCommand)
                {
                    State.CurrentCommandIndex++;
                    _skipNextCommand = false;
                }
                ScheduleRunNextCommand();
            }
        }

        private void ScheduleRunNextCommand()
        {
            if (_runScheduled)
                return;

            _runScheduled = true;
            CallDeferred(nameof(RunNextCommand));
        }

        private void NotifyStoryProgress()
        {
            WebUI.Instance?.SendStoryProgress();
        }

        public void OnDialogueComplete()
        {
            IsTyping = false;
            IsWaitingForInput = true;
        }

        public void OnChoiceMade(string targetSceneId)
        {
            if (string.IsNullOrEmpty(targetSceneId) || !Parser.Scenes.ContainsKey(targetSceneId))
            {
                GD.PushWarning($"选择分支目标场景不存在: {targetSceneId}");
                return;
            }

            State.CurrentSceneId = targetSceneId;
            State.CurrentCommandIndex = 0;
            IsTyping = false;
            IsWaitingForInput = false;
            _advanceRequested = false;
            _redirectRequested = false;
            _skipNextCommand = false;
            RunNextCommand();
        }

        public void JumpTo(string sceneId, int commandIndex = 0)
        {
            if (string.IsNullOrEmpty(sceneId) || !Parser.Scenes.ContainsKey(sceneId))
            {
                GD.PushWarning($"跳转目标场景不存在: {sceneId}");
                return;
            }

            State.CurrentSceneId = sceneId;
            State.CurrentCommandIndex = commandIndex;
            IsTyping = false;
            IsWaitingForInput = false;
            _advanceRequested = false;
            _skipNextCommand = false;
            _redirectRequested = true;
            ScheduleRunNextCommand();
        }

        /// <summary>
        /// 剧情管理器跳转：清除当前演出与输入状态后，从目标场景的指定指令重新开始执行。
        /// </summary>
        public void SeekTo(string sceneId, int commandIndex = 0)
        {
            if (string.IsNullOrEmpty(sceneId) || !Parser.Scenes.TryGetValue(sceneId, out var scene))
            {
                GD.PushWarning($"跳转目标场景不存在: {sceneId}");
                return;
            }

            int index = Math.Clamp(commandIndex, 0, scene.Commands.Count);
            CancelTransition();
            Audio.AudioManager.Instance?.StopVoice();
            DialogueBox?.ClearDialogue();
            DialogueBox?.ClearChoices();
            ResetStageForDebugSeek();

            IsAutoMode = false;
            IsSkipAllMode = false;
            IsSkipReadMode = false;
            IsWaitingForInput = false;
            IsTyping = false;
            _advanceRequested = false;
            _autoTimer = 0.0;
            _skipTimer = 0.0;
            _redirectRequested = false;
            _skipNextCommand = false;

            State.CurrentSceneId = sceneId;
            PrepareSceneStateToCommand(scene, index);
            State.CurrentCommandIndex = index;
            _skipNextCommand = false;

            WebUI.Instance?.Send(new { type = "auto_state", on = false });
            ScheduleRunNextCommand();
        }

        public void SkipNextCommand()
        {
            _skipNextCommand = true;
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

            CancelTransition();
            Audio.AudioManager.Instance?.StopVoice();
            DialogueBox?.ClearDialogue();
            DialogueBox?.ClearChoices();

            IsWaitingForInput = false;
            IsTyping = false;
            _advanceRequested = false;
            _autoTimer = 0.0;
            _skipTimer = 0.0;
            _redirectRequested = false;
            _skipNextCommand = false;

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

            // 统一通过延迟调度推进，避免读档时已有的 deferred 调用造成重复执行。
            ScheduleRunNextCommand();
        }

        private void RestoreStage(StageSnapshot stage)
        {
            if (!string.IsNullOrEmpty(stage.BackgroundPath))
            {
                var texture = GD.Load<Texture2D>($"res://assets/backgrounds/{stage.BackgroundPath}.png");
                if (texture != null)
                {
                    Background.Texture = texture;
                    Background.Modulate = Colors.White;
                }
                else
                {
                    GD.PushWarning($"读档时未找到背景资源: {stage.BackgroundPath}");
                    Background.Texture = null;
                    Background.Modulate = new Color(stage.BackgroundColor);
                }
            }
            else
            {
                Background.Texture = null;
                Background.Modulate = new Color(stage.BackgroundColor);
            }

            foreach (var child in CharacterStage.GetChildren())
                child.Free();

            // 恢复全屏 CG
            if (!string.IsNullOrEmpty(stage.CgPath))
                ShowCg(stage.CgPath, 0f);
            else
                HideCg(0f);

            foreach (var kv in stage.Characters)
            {
                var ch = kv.Value;
                if (!ch.Visible) continue;

                var sprite = new Sprite2D { Name = ch.CharacterId };
                CharacterStage.AddChild(sprite);
                sprite.Position = GetCharacterPosition(ch.Position);
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
            var manager = Audio.AudioManager.Instance;
            manager?.StopBgm(0f);

            // BgmPlaying 兼容旧版本存档：旧存档没有该字段时，只要有轨道名就视为正在播放。
            if ((audio.BgmPlaying || !string.IsNullOrEmpty(audio.BgmTrack)) && !string.IsNullOrEmpty(audio.BgmTrack))
                manager?.PlayBgm(audio.BgmTrack, 0f, audio.BgmLoop, audio.BgmPosition);
        }

        private void ResetStageForDebugSeek()
        {
            if (Background != null)
            {
                Background.Texture = null;
                Background.Modulate = Colors.Black;
            }

            if (CharacterStage != null)
            {
                foreach (var child in CharacterStage.GetChildren())
                    child.Free();
            }

            State.Stage = new StageSnapshot();
            State.Audio = new AudioSnapshot();
            HideCg(0f);
            Audio.AudioManager.Instance?.StopBgm(0f);
        }

        private void PrepareSceneStateToCommand(SceneData scene, int targetIndex)
        {
            int end = Math.Min(targetIndex, scene.Commands.Count);
            for (int i = 0; i < end; i++)
            {
                if (_skipNextCommand)
                {
                    _skipNextCommand = false;
                    continue;
                }

                Runner.PrepareCommand(scene.Commands[i]);
            }
        }

        public void CaptureAudioSnapshot()
        {
            var channel = Audio.AudioManager.Instance?.BgmChannel;
            if (channel == null)
                return;

            State.Audio.BgmTrack = channel.CurrentTrack;
            State.Audio.BgmLoop = channel.CurrentLoop;
            State.Audio.BgmPosition = channel.IsPlaying ? channel.CurrentPosition : 0f;
            State.Audio.BgmPlaying = channel.IsPlaying && !string.IsNullOrEmpty(channel.CurrentTrack);
        }

        /// <summary>
        /// 展示全屏 CG（res://assets/cg/{id}.png），带淡入。会记录到舞台快照用于存档。
        /// </summary>
        public void ShowCg(string id, float fade)
        {
            if (string.IsNullOrEmpty(id) || CgOverlay == null) return;

            var texture = GD.Load<Texture2D>($"res://assets/cg/{id}.png");
            if (texture == null)
            {
                GD.PushWarning($"未找到 CG 资源: {id}.png");
                return;
            }

            State.Stage.CgPath = id;
            CgOverlay.Texture = texture;
            CgOverlay.Visible = true;

            if (fade <= 0f)
            {
                CgOverlay.Modulate = Colors.White;
                return;
            }
            CgOverlay.Modulate = new Color(1, 1, 1, 0);
            var tween = CgOverlay.CreateTween();
            tween.TweenProperty(CgOverlay, "modulate:a", 1.0f, fade);
        }

        /// <summary>隐藏全屏 CG，带淡出。</summary>
        public void HideCg(float fade)
        {
            State.Stage.CgPath = string.Empty;
            if (CgOverlay == null || !CgOverlay.Visible) return;

            if (fade <= 0f)
            {
                CgOverlay.Visible = false;
                CgOverlay.Texture = null;
                CgOverlay.Modulate = new Color(1, 1, 1, 0);
                return;
            }
            var tween = CgOverlay.CreateTween();
            tween.TweenProperty(CgOverlay, "modulate:a", 0.0f, fade);
            tween.TweenCallback(Callable.From(() =>
            {
                CgOverlay.Visible = false;
                CgOverlay.Texture = null;
            }));
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
            if (!IsTransitioning)
                return;

            IsTransitioning = false;
            _backgroundTransitionTween = null;
            _advanceRequested = false;
            State.CurrentCommandIndex++;
            RunNextCommand();
        }

        /// <summary>
        /// 黑屏淡入淡出切换背景。动画完成后调用 onFinished。
        /// </summary>
        public void TransitionToBackground(Texture2D texture, Color backgroundColor, float duration, Action onFinished)
        {
            if (duration <= 0f)
            {
                Background.Texture = texture;
                Background.Modulate = texture != null ? Colors.White : backgroundColor;
                onFinished?.Invoke();
                return;
            }

            _transitionGeneration++;
            _backgroundTransitionTween?.Kill();

            var overlay = GetOrCreateTransitionOverlay();
            overlay.Color = Colors.Black;
            overlay.Modulate = new Color(1, 1, 1, 0);
            overlay.Visible = true;
            overlay.MouseFilter = Control.MouseFilterEnum.Ignore;

            float halfDuration = duration * 0.5f;
            int generation = _transitionGeneration;

            _backgroundTransitionTween = overlay.CreateTween();
            _backgroundTransitionTween.TweenProperty(overlay, "modulate:a", 1.0f, halfDuration);
            _backgroundTransitionTween.TweenCallback(Callable.From(() =>
            {
                if (generation != _transitionGeneration)
                    return;

                Background.Texture = texture;
                Background.Modulate = texture != null ? Colors.White : backgroundColor;
            }));
            _backgroundTransitionTween.TweenProperty(overlay, "modulate:a", 0.0f, halfDuration);
            _backgroundTransitionTween.TweenCallback(Callable.From(() =>
            {
                if (generation != _transitionGeneration)
                    return;

                overlay.Visible = false;
                _backgroundTransitionTween = null;
                onFinished?.Invoke();
            }));
        }

        public void CancelTransition()
        {
            _transitionGeneration++;
            _backgroundTransitionTween?.Kill();
            _backgroundTransitionTween = null;
            IsTransitioning = false;

            var overlay = TransitionLayer?.GetNodeOrNull<ColorRect>("Overlay");
            if (overlay != null)
            {
                overlay.Visible = false;
                overlay.Modulate = new Color(1, 1, 1, 0);
            }
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
            if (!State.IsRead(State.CurrentSceneId, State.CurrentCommandIndex))
                return false;

            // 快进只跳过不会改变游戏状态的展示指令。
            return command.Cmd == "say"
                || command.Cmd == "narrate"
                || command.Cmd == "se"
                || command.Cmd == "voice";
        }

        private bool IsBlockingVoice()
        {
            return Audio.AudioManager.Instance?.VoiceChannel?.IsPlaying ?? false;
        }

        private void ApplyVolumes()
        {
            Audio.AudioManager.Instance?.ApplyVolumes();
        }

        private void ValidateStoryAssets()
        {
            var checkedPaths = new HashSet<string>();

            foreach (var scene in Parser.Scenes.Values)
            {
                foreach (var command in scene.Commands)
                {
                    string path = string.Empty;
                    string description = command.Cmd;

                    switch (command.Cmd)
                    {
                        case "bg":
                            if (!string.IsNullOrEmpty(command.GetString("asset")))
                                path = $"res://assets/backgrounds/{command.GetString("asset")}.png";
                            break;
                        case "bgm":
                            if (!string.IsNullOrEmpty(command.GetString("track")))
                            {
                                // BGM 支持 ogg/mp3/wav，任一存在即视为有效
                                string trackName = command.GetString("track");
                                bool found = false;
                                foreach (string ext in new[] { "ogg", "mp3", "wav" })
                                {
                                    if (ResourceLoader.Exists($"res://assets/bgm/{trackName}.{ext}"))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (found) continue;
                                path = $"res://assets/bgm/{trackName}.(ogg|mp3|wav)";
                            }
                            break;
                        case "se":
                            if (!string.IsNullOrEmpty(command.GetString("sound")))
                                path = $"res://assets/se/{command.GetString("sound")}.wav";
                            break;
                        case "voice":
                            path = command.GetString("path");
                            break;
                        case "cgshow":
                            if (!string.IsNullOrEmpty(command.GetString("id")))
                                path = $"res://assets/cg/{command.GetString("id")}.png";
                            break;
                        case "show":
                            string character = command.GetString("character");
                            string emotion = command.GetString("emotion", "default");
                            if (!string.IsNullOrEmpty(character) && !string.IsNullOrEmpty(emotion))
                                path = $"res://assets/characters/{character}/{emotion}.png";
                            break;
                    }

                    if (string.IsNullOrEmpty(path) || !checkedPaths.Add(path))
                        continue;

                    if (!ResourceLoader.Exists(path))
                        GD.PushWarning($"剧情资源缺失（{description}）: {path}");
                }
            }
        }

        public Vector2 GetCharacterPosition(string position)
        {
            Viewport viewport = GetViewport();
            Vector2 size = viewport.GetVisibleRect().Size;
            string normalized = (position ?? "center").ToLowerInvariant();

            return normalized switch
            {
                "left" => new Vector2(size.X * 0.25f, size.Y * 0.58f),
                "right" => new Vector2(size.X * 0.75f, size.Y * 0.58f),
                "center" => new Vector2(size.X * 0.5f, size.Y * 0.58f),
                _ => new Vector2(size.X * 0.5f, size.Y * 0.58f)
            };
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
