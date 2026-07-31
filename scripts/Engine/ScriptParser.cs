using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace 交互式文本.Engine
{
    /// <summary>
    /// JSON 剧情解析器。将 story/xxx.json 解析为 SceneData 与 CommandData。
    /// </summary>
    public partial class ScriptParser : RefCounted
    {
        public System.Collections.Generic.Dictionary<string, CharacterData> Characters { get; private set; } = new();
        public System.Collections.Generic.Dictionary<string, SceneData> Scenes { get; private set; } = new();

        public Error LoadFromJson(string filePath)
        {
            var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PushError($"无法打开剧情文件: {filePath}");
                return FileAccess.GetOpenError();
            }

            string jsonText = file.GetAsText();
            file.Close();

            var json = new Json();
            Error parseErr = json.Parse(jsonText);
            if (parseErr != Error.Ok)
            {
                GD.PushError($"JSON 解析失败: {json.GetErrorMessage()} 位于行 {json.GetErrorLine()}");
                return parseErr;
            }

            var root = json.Data.AsGodotDictionary();
            ParseCharacters(SafeGet(root, "characters").AsGodotDictionary());
            ParseScenes(SafeGet(root, "scenes").AsGodotDictionary());

            return Error.Ok;
        }

        private static Variant SafeGet(Dictionary dict, string key)
        {
            return dict.ContainsKey(key) ? dict[key] : default;
        }

        private void ParseCharacters(Dictionary charDict)
        {
            Characters.Clear();
            foreach (var kv in charDict)
            {
                string id = kv.Key.ToString();
                var data = kv.Value.AsGodotDictionary();
                Characters[id] = new CharacterData
                {
                    Id = id,
                    DisplayName = SafeGet(data, "displayName").AsString(),
                    Color = SafeGet(data, "color").AsString(),
                    DefaultPosition = SafeGet(data, "defaultPosition").AsString()
                };

                if (string.IsNullOrEmpty(Characters[id].DisplayName))
                    Characters[id].DisplayName = id;
                if (string.IsNullOrEmpty(Characters[id].Color))
                    Characters[id].Color = "#ffffff";
                if (string.IsNullOrEmpty(Characters[id].DefaultPosition))
                    Characters[id].DefaultPosition = "center";
            }
        }

        private void ParseScenes(Dictionary sceneDict)
        {
            Scenes.Clear();
            foreach (var kv in sceneDict)
            {
                string id = kv.Key.ToString();
                var data = kv.Value.AsGodotDictionary();
                var scene = new SceneData { Id = id };

                var commands = SafeGet(data, "commands").AsGodotArray();
                foreach (var cmdVariant in commands)
                {
                    var cmdDict = cmdVariant.AsGodotDictionary();
                    scene.Commands.Add(CommandData.FromDictionary(cmdDict));
                }

                Scenes[id] = scene;
            }
        }
    }

    public class CharacterData
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Color { get; set; } = "#ffffff";
        public string DefaultPosition { get; set; } = "center";
    }

    public class SceneData
    {
        public string Id { get; set; } = string.Empty;
        public List<CommandData> Commands { get; set; } = new();
    }

    public class CommandData
    {
        public string Cmd { get; set; } = string.Empty;
        public System.Collections.Generic.Dictionary<string, Variant> Args { get; set; } = new();

        public static CommandData FromDictionary(Dictionary dict)
        {
            var cmd = new CommandData();
            if (dict.ContainsKey("cmd"))
                cmd.Cmd = dict["cmd"].AsString();

            foreach (var kv in dict)
            {
                string key = kv.Key.ToString();
                if (key != "cmd")
                    cmd.Args[key] = kv.Value;
            }

            return cmd;
        }

        public Variant GetArg(string key, Variant defaultValue = default)
        {
            if (Args.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public string GetString(string key, string defaultValue = "")
        {
            if (Args.TryGetValue(key, out var value))
                return value.AsString();
            return defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (Args.TryGetValue(key, out var value))
                return value.AsSingle();
            return defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (Args.TryGetValue(key, out var value))
                return value.AsBool();
            return defaultValue;
        }
    }
}
