using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace 交互式文本.Engine
{
    /// <summary>
    /// 存档/读档管理器。处理序列化与反序列化。
    /// </summary>
    public partial class SaveLoadManager : RefCounted
    {
        private static readonly string SaveDir = "user://saves";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new Data.VariantConverter() }
        };

        public static string GetSavePath(int slot)
        {
            return System.IO.Path.Combine(SaveDir, $"save_{slot}.json");
        }

        public static void Save(int slot, VNCore core)
        {
            EnsureDirectory();
            core.CaptureAudioSnapshot();

            var data = new Data.SaveData
            {
                CurrentSceneId = core.State.CurrentSceneId,
                CurrentCommandIndex = core.State.CurrentCommandIndex,
                Variables = core.State.Variables,
                ReadFlags = core.State.ReadFlags,
                History = core.State.History,
                Stage = core.State.Stage,
                Audio = core.State.Audio,
                Unlocks = core.State.Unlocks
            };

            string json = JsonSerializer.Serialize(data, JsonOptions);
            var file = FileAccess.Open(GetSavePath(slot), FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PushError($"无法写入存档槽 {slot}");
                return;
            }
            file.StoreString(json);
            file.Close();
        }

        public static Data.SaveData Load(int slot)
        {
            string path = GetSavePath(slot);
            if (!FileAccess.FileExists(path))
                return null;

            var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null) return null;

            try
            {
                string json = file.GetAsText();
                return JsonSerializer.Deserialize<Data.SaveData>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                GD.PushError($"读取存档失败: {ex.Message}");
                return null;
            }
            finally
            {
                file.Close();
            }
        }

        public static void Delete(int slot)
        {
            string path = GetSavePath(slot);
            if (FileAccess.FileExists(path))
                DirAccess.RemoveAbsolute(path);
        }

        public static void EnsureDirectory()
        {
            if (!DirAccess.DirExistsAbsolute(SaveDir))
                DirAccess.MakeDirAbsolute(SaveDir);
        }
    }
}
