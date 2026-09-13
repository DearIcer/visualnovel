using Godot;
using System;
using System.Collections.Generic;

namespace 交互式文本.Engine
{
    /// <summary>
    /// 游戏状态：变量、历史记录、已读标记、解锁数据。
    /// 所有字段均需可序列化，用于存档/读档。
    /// </summary>
    public partial class GameState : RefCounted
    {
        // 剧情变量：int / float / bool / string
        public Dictionary<string, Variant> Variables { get; set; } = new();

        // 已读标记：key = sceneId + ":" + commandIndex
        public HashSet<string> ReadFlags { get; set; } = new();

        // 历史记录（最大容量由外部控制）
        public List<HistoryEntry> History { get; set; } = new();

        // 当前位置
        public string CurrentSceneId { get; set; } = string.Empty;
        public int CurrentCommandIndex { get; set; } = 0;

        // 演出快照（用于读档复现）
        public StageSnapshot Stage { get; set; } = new();

        // 音频快照
        public AudioSnapshot Audio { get; set; } = new();

        // 解锁记录
        public UnlockData Unlocks { get; set; } = new();

        public void SetVariable(string name, Variant value)
        {
            Variables[name] = value;
        }

        public Variant GetVariable(string name)
        {
            if (Variables.TryGetValue(name, out var value))
                return value;
            return default;
        }

        public void MarkRead(string sceneId, int commandIndex)
        {
            ReadFlags.Add($"{sceneId}:{commandIndex}");
        }

        public bool IsRead(string sceneId, int commandIndex)
        {
            return ReadFlags.Contains($"{sceneId}:{commandIndex}");
        }

        public void PushHistory(HistoryEntry entry, int maxCapacity = 200)
        {
            History.Add(entry);
            while (History.Count > maxCapacity)
                History.RemoveAt(0);
        }
    }

    public class HistoryEntry
    {
        public string SpeakerId { get; set; } = string.Empty;
        public string SpeakerName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string VoicePath { get; set; } = string.Empty;
        public string SceneId { get; set; } = string.Empty;
        public int CommandIndex { get; set; } = 0;
    }

    public class StageSnapshot
    {
        public string BackgroundPath { get; set; } = string.Empty;
        public string BackgroundColor { get; set; } = "#000000";
        /// <summary>当前正在展示的全屏 CG（res://assets/cg 下的文件名，空串表示无）。</summary>
        public string CgPath { get; set; } = string.Empty;
        public Dictionary<string, CharacterOnStage> Characters { get; set; } = new();
    }

    public class CharacterOnStage
    {
        public string CharacterId { get; set; } = string.Empty;
        public string Emotion { get; set; } = string.Empty;
        public string Position { get; set; } = "center";
        public bool Visible { get; set; } = true;
    }

    public class AudioSnapshot
    {
        public string BgmTrack { get; set; } = string.Empty;
        public float BgmPosition { get; set; } = 0f;
        public bool BgmLoop { get; set; } = true;
        public bool BgmPlaying { get; set; } = false;
    }

    public class UnlockData
    {
        public HashSet<string> Cgs { get; set; } = new();
        public HashSet<string> Musics { get; set; } = new();
        public HashSet<string> Voices { get; set; } = new();
    }
}
