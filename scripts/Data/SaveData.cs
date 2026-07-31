using Godot;
using System;
using System.Collections.Generic;

namespace 交互式文本.Data
{
    /// <summary>
    /// 存档数据结构，包含完整游戏状态，用于读档复现。
    /// </summary>
    public class SaveData
    {
        public string Version { get; set; } = "1.0";
        public DateTime SaveTime { get; set; } = DateTime.UtcNow;

        public string CurrentSceneId { get; set; } = string.Empty;
        public int CurrentCommandIndex { get; set; } = 0;

        public Dictionary<string, Variant> Variables { get; set; } = new();
        public HashSet<string> ReadFlags { get; set; } = new();
        public List<Engine.HistoryEntry> History { get; set; } = new();

        public Engine.StageSnapshot Stage { get; set; } = new();
        public Engine.AudioSnapshot Audio { get; set; } = new();
        public Engine.UnlockData Unlocks { get; set; } = new();
    }
}
