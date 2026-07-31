using Godot;
using System;
using System.Text.Json;

namespace 交互式文本.Data
{
    /// <summary>
    /// 全局游戏设置：音量、窗口、自动播放延迟等。
    /// </summary>
    public class GameConfig
    {
        public float MasterVolume { get; set; } = 1.0f;
        public float BgmVolume { get; set; } = 0.8f;
        public float SeVolume { get; set; } = 0.9f;
        public float VoiceVolume { get; set; } = 1.0f;

        public float AutoDelay { get; set; } = 2.0f;
        public float SkipDelay { get; set; } = 0.05f;

        public bool IsFullscreen { get; set; } = false;
        public int TargetFps { get; set; } = 60;

        private static readonly string ConfigPath = "user://settings.json";

        public static GameConfig Load()
        {
            var config = new GameConfig();
            if (!FileAccess.FileExists(ConfigPath))
                return config;

            var file = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Read);
            if (file == null) return config;

            try
            {
                var data = JsonSerializer.Deserialize<GameConfig>(file.GetAsText());
                if (data != null) return data;
            }
            catch (Exception ex)
            {
                GD.PushWarning($"读取设置失败: {ex.Message}");
            }
            finally
            {
                file.Close();
            }

            return config;
        }

        public void Save()
        {
            var file = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PushError($"无法保存设置: {ConfigPath}");
                return;
            }
            file.StoreString(JsonSerializer.Serialize(this));
            file.Close();
        }
    }
}
