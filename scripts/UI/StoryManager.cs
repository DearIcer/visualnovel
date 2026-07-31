using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using 交互式文本.Engine;

namespace 交互式文本
{
    /// <summary>
    /// 剧情调试管理器：汇总场景与指令时间线，供 Web UI 显示进度并执行跳转。
    /// </summary>
    public partial class StoryManager : RefCounted
    {
        private readonly VNCore _core;
        private List<SceneEntry> _scenes;

        public StoryManager(VNCore core)
        {
            _core = core;
        }

        /// <summary>构建剧情时间线元数据，包括场景列表、指令总量与当前位置。</summary>
        public object BuildMeta()
        {
            EnsureIndex();
            return new
            {
                type = "story_meta",
                scenes = _scenes.Select(s => new
                {
                    id = s.Id,
                    commandCount = s.CommandCount,
                    startIndex = s.StartIndex
                }).ToArray(),
                totalCommands = GetTotalCommandCount(),
                progress = BuildProgress()
            };
        }

        /// <summary>构建当前剧情进度，供前端进度条持续刷新。</summary>
        public object BuildProgress()
        {
            EnsureIndex();

            var state = _core.State;
            int sceneIndex = FindSceneIndex(state.CurrentSceneId);
            int commandCount = sceneIndex >= 0 ? _scenes[sceneIndex].CommandCount : 0;
            int commandIndex = sceneIndex >= 0 ? Math.Clamp(state.CurrentCommandIndex, 0, commandCount) : 0;
            int globalIndex = sceneIndex >= 0 ? _scenes[sceneIndex].StartIndex + commandIndex : 0;
            string commandLabel = sceneIndex >= 0 && commandIndex < commandCount
                ? DescribeCommand(_scenes[sceneIndex].Commands[commandIndex])
                : string.Empty;

            return new
            {
                type = "story_progress",
                sceneId = state.CurrentSceneId,
                sceneIndex,
                sceneCount = _scenes.Count,
                commandIndex,
                commandCount,
                globalIndex,
                totalCommands = GetTotalCommandCount(),
                commandLabel
            };
        }

        /// <summary>按场景与指令索引跳转。</summary>
        public void Seek(string sceneId, int commandIndex)
        {
            _core.SeekTo(sceneId, commandIndex);
        }

        /// <summary>按全局指令索引跳转，索引由前端进度条产生。</summary>
        public void SeekGlobal(int globalIndex)
        {
            EnsureIndex();
            int totalCommands = GetTotalCommandCount();
            if (totalCommands <= 0)
                return;

            globalIndex = Math.Clamp(globalIndex, 0, totalCommands - 1);

            for (int i = _scenes.Count - 1; i >= 0; i--)
            {
                var scene = _scenes[i];
                if (globalIndex < scene.StartIndex)
                    continue;

                if (scene.CommandCount == 0)
                    continue;

                int commandIndex = Math.Clamp(globalIndex - scene.StartIndex, 0, scene.CommandCount - 1);
                _core.SeekTo(scene.Id, commandIndex);
                return;
            }
        }

        private void EnsureIndex()
        {
            if (_core?.Parser == null)
            {
                _scenes ??= new List<SceneEntry>();
                return;
            }

            if (_scenes != null && (_scenes.Count > 0 || _core.Parser.Scenes.Count == 0))
                return;

            _scenes = new List<SceneEntry>();
            int startIndex = 0;
            foreach (var kv in _core.Parser.Scenes)
            {
                _scenes.Add(new SceneEntry
                {
                    Id = kv.Key,
                    Commands = kv.Value.Commands,
                    StartIndex = startIndex
                });
                startIndex += kv.Value.Commands.Count;
            }
        }

        private int FindSceneIndex(string sceneId)
        {
            for (int i = 0; i < _scenes.Count; i++)
            {
                if (_scenes[i].Id == sceneId)
                    return i;
            }
            return -1;
        }

        private int GetTotalCommandCount()
        {
            return _scenes?.Sum(s => s.CommandCount) ?? 0;
        }

        private static string DescribeCommand(CommandData command)
        {
            string text = command.GetString("text");
            switch (command.Cmd)
            {
                case "say":
                    return $"{command.GetString("character")}：{Truncate(text)}";
                case "narrate":
                    return Truncate(text);
                case "bg":
                    return $"背景 {command.GetString("asset")}";
                case "bgm":
                    return $"音乐 {command.GetString("track")}";
                case "se":
                    return $"音效 {command.GetString("sound")}";
                case "voice":
                    return $"语音 {command.GetString("path")}";
                case "show":
                    return $"立绘 {command.GetString("character")}/{command.GetString("emotion", "default")}";
                case "hide":
                    return $"隐藏 {command.GetString("character")}";
                case "move":
                    return $"移动 {command.GetString("character")} -> {command.GetString("position", "center")}";
                case "choice":
                    return $"选择支 {command.GetArg("options").AsGodotArray().Count} 项";
                case "jump":
                    return $"跳转 {command.GetString("target")}";
                case "set":
                    return $"变量 {command.GetString("name")} = {command.GetArg("value")}";
                case "if":
                    return $"条件 {command.GetString("name")} {command.GetString("op", "==")} {command.GetArg("value")}";
                case "cg":
                case "music":
                case "vo_unlock":
                    return $"解锁 {command.GetString("id")}";
                default:
                    return command.Cmd;
            }
        }

        private static string Truncate(string value, int maxLength = 64)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private sealed class SceneEntry
        {
            public string Id;
            public List<CommandData> Commands;
            public int StartIndex;

            public int CommandCount => Commands?.Count ?? 0;
        }
    }
}
