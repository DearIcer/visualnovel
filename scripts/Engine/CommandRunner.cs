using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace 交互式文本.Engine
{
    /// <summary>
    /// 指令执行器。将 CommandData 映射为具体的演出与状态变更。
    /// </summary>
    public partial class CommandRunner : RefCounted
    {
        private readonly VNCore _core;
        private bool _prepareOnly = false;

        public CommandRunner(VNCore core)
        {
            _core = core;
        }

        /// <summary>仅应用状态类指令，不播放对白、音效、动画或跳转，用于调试跳转前恢复场景状态。</summary>
        public void PrepareCommand(CommandData cmd)
        {
            _prepareOnly = true;
            try
            {
                Execute(cmd);
            }
            finally
            {
                _prepareOnly = false;
            }
        }

        public void Execute(CommandData cmd)
        {
            switch (cmd.Cmd)
            {
                case "bg":
                    ExecuteBackground(cmd);
                    break;
                case "bgm":
                    ExecuteBgm(cmd);
                    break;
                case "se":
                    ExecuteSe(cmd);
                    break;
                case "voice":
                    ExecuteVoice(cmd);
                    break;
                case "show":
                    ExecuteShow(cmd);
                    break;
                case "hide":
                    ExecuteHide(cmd);
                    break;
                case "move":
                    ExecuteMove(cmd);
                    break;
                case "say":
                    ExecuteSay(cmd);
                    break;
                case "narrate":
                    ExecuteNarrate(cmd);
                    break;
                case "choice":
                    ExecuteChoice(cmd);
                    break;
                case "jump":
                    ExecuteJump(cmd);
                    break;
                case "set":
                    ExecuteSet(cmd);
                    break;
                case "if":
                    ExecuteIf(cmd);
                    break;
                case "cg":
                case "music":
                case "vo_unlock":
                    ExecuteUnlock(cmd);
                    break;
                default:
                    GD.PushWarning($"未知指令: {cmd.Cmd}");
                    break;
            }
        }

        private void ExecuteBackground(CommandData cmd)
        {
            string color = cmd.GetString("color", "#000000");
            string asset = cmd.GetString("asset");
            float fade = _prepareOnly ? 0f : cmd.GetFloat("fade", 0f);
            Color backgroundColor;

            try
            {
                backgroundColor = new Color(color);
            }
            catch
            {
                GD.PushWarning($"背景颜色无效: {color}，将使用黑色");
                backgroundColor = Colors.Black;
                color = "#000000";
            }

            Texture2D texture = null;

            if (!string.IsNullOrEmpty(asset))
            {
                texture = GD.Load<Texture2D>($"res://assets/backgrounds/{asset}.png");
                if (texture != null)
                {
                    if (fade > 0f)
                    {
                        // 带 fade 参数时使用黑屏淡入淡出切换背景，并阻塞剧情自动推进
                        _core.IsTransitioning = true;
                        _core.TransitionToBackground(texture, Colors.White, fade, () => _core.OnTransitionFinished());
                    }
                    else
                    {
                        _core.Background.Texture = texture;
                        _core.Background.Modulate = Colors.White;
                    }
                }
                else
                {
                    GD.PushWarning($"未找到背景资源: {asset}.png，将回退为纯色背景");
                    asset = string.Empty;
                }
            }

            if (texture == null)
            {
                if (fade > 0f)
                {
                    _core.IsTransitioning = true;
                    _core.TransitionToBackground(null, backgroundColor, fade, () => _core.OnTransitionFinished());
                }
                else
                {
                    _core.Background.Texture = null;
                    _core.Background.Modulate = backgroundColor;
                }
            }

            _core.State.Stage.BackgroundColor = color;
            _core.State.Stage.BackgroundPath = asset;
        }

        private void ExecuteBgm(CommandData cmd)
        {
            string action = cmd.GetString("action", "play");
            string track = cmd.GetString("track");
            float fade = _prepareOnly ? 0f : cmd.GetFloat("fade", 1.0f);
            bool loop = cmd.GetBool("loop", true);
            bool started = false;

            switch (action)
            {
                case "play":
                    started = Audio.AudioManager.Instance?.PlayBgm(track, fade, loop) ?? false;
                    break;
                case "crossfade":
                    started = Audio.AudioManager.Instance?.CrossfadeBgm(track, fade, loop) ?? false;
                    break;
                case "stop":
                    Audio.AudioManager.Instance?.StopBgm(fade);
                    _core.State.Audio.BgmTrack = string.Empty;
                    _core.State.Audio.BgmPosition = 0f;
                    _core.State.Audio.BgmPlaying = false;
                    break;
                default:
                    GD.PushWarning($"未知 BGM 操作: {action}");
                    break;
            }

            if (action == "play" || action == "crossfade")
            {
                _core.State.Audio.BgmTrack = started ? track : string.Empty;
                _core.State.Audio.BgmPosition = 0f;
                _core.State.Audio.BgmLoop = loop;
                _core.State.Audio.BgmPlaying = started;
            }
        }

        private void ExecuteSe(CommandData cmd)
        {
            if (_prepareOnly) return;

            string sound = cmd.GetString("sound");
            float volume = cmd.GetFloat("volume", 1.0f);
            float pitch = cmd.GetFloat("pitch", 1.0f);
            Audio.AudioManager.Instance?.PlaySe(sound, volume, pitch);
        }

        private void ExecuteVoice(CommandData cmd)
        {
            if (_prepareOnly) return;

            string path = cmd.GetString("path");
            Audio.AudioManager.Instance?.PlayVoice(path);
        }

        private void ExecuteShow(CommandData cmd)
        {
            string charId = cmd.GetString("character");
            string position = cmd.GetString("position");
            string emotion = cmd.GetString("emotion", "default");
            string animation = cmd.GetString("animation", "fade");
            float duration = cmd.GetFloat("duration", 0.3f);

            if (!_core.Parser.Characters.TryGetValue(charId, out var character))
            {
                GD.PushWarning($"未定义角色: {charId}");
                return;
            }

            if (string.IsNullOrEmpty(position))
                position = character.DefaultPosition;

            var texture = GD.Load<Texture2D>($"res://assets/characters/{charId}/{emotion}.png");
            if (texture == null)
            {
                GD.PushWarning($"未找到立绘资源: {charId}/{emotion}.png");
                return;
            }

            if (_prepareOnly)
            {
                var preparedSprite = GetOrCreateCharacterSprite(charId);
                preparedSprite.Position = _core.GetCharacterPosition(position);
                preparedSprite.Texture = texture;
                preparedSprite.Visible = true;
                preparedSprite.Modulate = Colors.White;
                _core.ApplyCharacterScale(preparedSprite);
                _core.State.Stage.Characters[charId] = new CharacterOnStage
                {
                    CharacterId = charId,
                    Emotion = emotion,
                    Position = position,
                    Visible = true
                };
                return;
            }

            var existingSprite = _core.CharacterStage.GetNodeOrNull<Sprite2D>(charId);
            var sprite = GetOrCreateCharacterSprite(charId);
            sprite.Position = _core.GetCharacterPosition(position);
            sprite.Visible = true;

            if (existingSprite != null && existingSprite.Texture != null && existingSprite.Texture != texture)
            {
                // 同角色切换表情：交叉淡入淡出
                CrossfadeCharacter(sprite, texture, animation, duration);
            }
            else
            {
                // 新登场或重复 show：直接设置并播放登场动画
                sprite.Texture = texture;
                _core.ApplyCharacterScale(sprite);
                PlayCharacterAnimation(sprite, animation, duration);
            }

            _core.State.Stage.Characters[charId] = new CharacterOnStage
            {
                CharacterId = charId,
                Emotion = emotion,
                Position = position,
                Visible = true
            };
        }

        private void ExecuteHide(CommandData cmd)
        {
            string charId = cmd.GetString("character");
            var sprite = GetOrCreateCharacterSprite(charId);
            sprite.Visible = false;

            if (_core.State.Stage.Characters.ContainsKey(charId))
                _core.State.Stage.Characters[charId].Visible = false;
        }

        private void ExecuteMove(CommandData cmd)
        {
            string charId = cmd.GetString("character");
            string position = cmd.GetString("position", "center");
            float duration = cmd.GetFloat("duration", 0.5f);

            var sprite = GetOrCreateCharacterSprite(charId);
            if (_prepareOnly)
            {
                sprite.Position = _core.GetCharacterPosition(position);
            }
            else
            {
                var tween = sprite.CreateTween();
                tween.TweenProperty(sprite, "position", _core.GetCharacterPosition(position), duration);
            }

            if (_core.State.Stage.Characters.ContainsKey(charId))
                _core.State.Stage.Characters[charId].Position = position;
        }

        private void ExecuteSay(CommandData cmd)
        {
            if (_prepareOnly) return;

            string charId = cmd.GetString("character");
            string text = cmd.GetString("text");

            string speakerName = charId;
            string color = "#ffffff";
            if (_core.Parser.Characters.TryGetValue(charId, out var character))
            {
                speakerName = character.DisplayName;
                color = character.Color;
            }

            _core.IsTyping = true;
            _core.DialogueBox.ShowDialogue(speakerName, text, color);

            _core.State.PushHistory(new HistoryEntry
            {
                SpeakerId = charId,
                SpeakerName = speakerName,
                Text = text,
                SceneId = _core.State.CurrentSceneId,
                CommandIndex = _core.State.CurrentCommandIndex
            });
        }

        private void ExecuteNarrate(CommandData cmd)
        {
            if (_prepareOnly) return;

            string text = cmd.GetString("text");
            _core.IsTyping = true;
            _core.DialogueBox.ShowDialogue("", text, "#aaaaaa");

            _core.State.PushHistory(new HistoryEntry
            {
                SpeakerId = string.Empty,
                SpeakerName = string.Empty,
                Text = text,
                SceneId = _core.State.CurrentSceneId,
                CommandIndex = _core.State.CurrentCommandIndex
            });
        }

        private void ExecuteChoice(CommandData cmd)
        {
            if (_prepareOnly) return;

            var options = new List<ChoiceOption>();
            var args = cmd.GetArg("options").AsGodotArray();
            foreach (var opt in args)
            {
                var dict = opt.AsGodotDictionary();
                options.Add(new ChoiceOption
                {
                    Text = dict.ContainsKey("text") ? dict["text"].AsString() : "",
                    Target = dict.ContainsKey("target") ? dict["target"].AsString() : ""
                });
            }
            _core.DialogueBox.ShowChoices(options);
            _core.IsWaitingForInput = true;
        }

        private void ExecuteJump(CommandData cmd)
        {
            if (_prepareOnly) return;

            string target = cmd.GetString("target");
            _core.JumpTo(target);
        }

        private void ExecuteSet(CommandData cmd)
        {
            string name = cmd.GetString("name");
            Variant value = cmd.GetArg("value");
            _core.State.SetVariable(name, value);
            GD.Print($"[SET] {name} = {value}");
        }

        private void ExecuteIf(CommandData cmd)
        {
            // MVP 阶段：简单支持单一变量布尔/数值比较
            string name = cmd.GetString("name");
            string op = cmd.GetString("op", "==");
            Variant value = cmd.GetArg("value");
            Variant current = _core.State.GetVariable(name);

            bool result = EvaluateCondition(current, op, value);
            if (!result)
            {
                // MVP 不使用 endif 块：条件不满足时跳过紧随其后的单条指令。
                _core.SkipNextCommand();
                GD.Print($"[IF] 条件不满足，跳过下一条指令: {name} {op} {value}");
            }
        }

        private void ExecuteUnlock(CommandData cmd)
        {
            string id = cmd.GetString("id");
            switch (cmd.Cmd)
            {
                case "cg": _core.State.Unlocks.Cgs.Add(id); break;
                case "music": _core.State.Unlocks.Musics.Add(id); break;
                case "vo_unlock": _core.State.Unlocks.Voices.Add(id); break;
            }
            GD.Print($"[UNLOCK] {cmd.Cmd}: {id}");
        }

        private Sprite2D GetOrCreateCharacterSprite(string charId)
        {
            var node = _core.CharacterStage.GetNodeOrNull<Sprite2D>(charId);
            if (node == null)
            {
                node = new Sprite2D { Name = charId };
                _core.CharacterStage.AddChild(node);
            }
            return node;
        }

        /// <summary>
        /// 同角色切换表情：用旧图副本做淡出，新图做淡入，实现交叉淡化。
        /// </summary>
        private void CrossfadeCharacter(Sprite2D sprite, Texture2D newTexture, string animation, float duration)
        {
            if (duration <= 0f)
            {
                sprite.Texture = newTexture;
                _core.ApplyCharacterScale(sprite);
                sprite.Modulate = Colors.White;
                return;
            }

            // 清理可能残留的旧过渡精灵
            string oldName = $"{sprite.Name}_old";
            var oldNode = _core.CharacterStage.GetNodeOrNull<Sprite2D>(oldName);
            oldNode?.QueueFree();

            // 创建旧图副本用于淡出
            var oldSprite = new Sprite2D
            {
                Name = oldName,
                Texture = sprite.Texture,
                Position = sprite.Position,
                Scale = sprite.Scale,
                Modulate = sprite.Modulate,
                Visible = true
            };
            _core.CharacterStage.AddChild(oldSprite);

            // 切换为新图并从透明开始淡入
            sprite.Texture = newTexture;
            _core.ApplyCharacterScale(sprite);
            sprite.Modulate = new Color(1, 1, 1, 0);

            PlayTransitionAnimation(oldSprite, sprite, animation, duration);
        }

        /// <summary>
        /// 角色登场时播放的动画接口。目前仅实现 fade，可在此扩展 slide / shake 等。
        /// </summary>
        private void PlayCharacterAnimation(Sprite2D sprite, string animation, float duration)
        {
            if (duration <= 0f)
            {
                sprite.Modulate = Colors.White;
                return;
            }

            switch (animation.ToLower())
            {
                case "fade":
                default:
                    if (animation.ToLower() != "fade")
                        GD.PushWarning($"未实现的角色动画: {animation}，已回退为 fade。");

                    sprite.Modulate = new Color(1, 1, 1, 0);
                    var tween = sprite.CreateTween();
                    tween.TweenProperty(sprite, "modulate:a", 1.0f, duration);
                    break;
            }
        }

        /// <summary>
        /// 角色表情切换时的过渡动画接口。目前仅实现 fade，可在此扩展其他过渡。
        /// </summary>
        private void PlayTransitionAnimation(Sprite2D oldSprite, Sprite2D newSprite, string animation, float duration)
        {
            switch (animation.ToLower())
            {
                case "fade":
                default:
                    if (animation.ToLower() != "fade")
                        GD.PushWarning($"未实现的角色过渡动画: {animation}，已回退为 fade。");

                    var fadeOut = oldSprite.CreateTween();
                    fadeOut.TweenProperty(oldSprite, "modulate:a", 0.0f, duration);
                    fadeOut.Finished += () => oldSprite.QueueFree();

                    var fadeIn = newSprite.CreateTween();
                    fadeIn.TweenProperty(newSprite, "modulate:a", 1.0f, duration);
                    break;
            }
        }

        private bool EvaluateCondition(Variant left, string op, Variant right)
        {
            try
            {
                return op switch
                {
                    "==" => left.Equals(right),
                    "!=" => !left.Equals(right),
                    ">" => left.AsSingle() > right.AsSingle(),
                    ">=" => left.AsSingle() >= right.AsSingle(),
                    "<" => left.AsSingle() < right.AsSingle(),
                    "<=" => left.AsSingle() <= right.AsSingle(),
                    _ => false
                };
            }
            catch
            {
                return false;
            }
        }
    }

    public class ChoiceOption
    {
        public string Text { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }
}
