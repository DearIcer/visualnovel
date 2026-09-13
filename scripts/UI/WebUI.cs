using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using 交互式文本.Data;
using 交互式文本.Engine;

namespace 交互式文本
{
	/// <summary>
	/// Web UI 桥接层：连接 godot_wry WebView 与游戏引擎。
	/// 负责 C# ↔ JS 的 JSON 消息收发、各面板（历史/存读档/设置/鉴赏）的数据供给与操作分发。
	/// </summary>
	public partial class WebUI : Control
	{
		/// <summary>全局单例。</summary>
		public static WebUI Instance { get; private set; }

		/// <summary>存档槽位数量。</summary>
		[Export] public int SlotCount = 6;

		/// <summary>是否有任意 Web 面板处于打开状态（由 JS 的 panel_opened/panel_closed 维护）。</summary>
		public bool IsAnyUIOpen => !string.IsNullOrEmpty(_openPanel);

		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		private Node _webView;
		private VNCore _core;
		private DialogueBox _dialogueBox;
		private StoryManager _storyManager;
		private bool _pageReady = false;
		private readonly List<string> _pendingMessages = new();
		private string _openPanel = string.Empty;

		public override void _Ready()
		{
			Instance = this;
			_core = GetNode<VNCore>("/root/Main");
			_storyManager = new StoryManager(_core);
			_dialogueBox = GetNodeOrNull<DialogueBox>("../DialogueBox");
			_webView = GetNodeOrNull("../WebView");

			if (_webView == null)
			{
				GD.PushError("WebUI: 未找到 WebView 节点，Web UI 将无法工作。");
				return;
			}

			_webView.Connect("ipc_message", Callable.From<string>(OnIpcMessage));
			_webView.Connect("page_load_finished", Callable.From<string>(OnPageLoadFinished));
		}

		/// <summary>向 Web 页面发送一条 JSON 消息；页面未就绪时排队等待。</summary>
		public void Send(object msg)
		{
			string json = JsonSerializer.Serialize(msg, JsonOptions);
			if (_pageReady)
				_webView?.Call("post_message", json);
			else
				_pendingMessages.Add(json);
		}

		/// <summary>转发对话内容到 Web 对话框。</summary>
		public void ShowDialogue(string speakerName, string text, string nameColorHex)
		{
			Send(new Dictionary<string, object>
			{
				["type"] = "dialogue",
				["speaker"] = speakerName ?? string.Empty,
				["text"] = text ?? string.Empty,
				["color"] = string.IsNullOrEmpty(nameColorHex) ? "#ffffff" : nameColorHex
			});
		}

		/// <summary>转发选择支到 Web 页面。</summary>
		public void ShowChoices(List<ChoiceOption> options)
		{
			Send(new
			{
				type = "choices",
				options = options.Select(o => new { text = o.Text, target = o.Target }).ToArray()
			});
		}

		/// <summary>打开指定面板：先推送数据，再命令 Web 端打开。</summary>
		public void OpenPanel(string panel)
		{
			switch (panel)
			{
				case "history": SendHistory(); break;
				case "save": SendSaveSlots("save"); break;
				case "load": SendSaveSlots("load"); break;
				case "settings": SendSettings(); break;
				case "gallery": SendGallery(); break;
				default:
					GD.PushWarning($"WebUI: 未知面板 {panel}");
					return;
			}
			Send(new { type = "open_panel", panel });
		}

		/// <summary>关闭所有面板。</summary>
		public void CloseAll()
		{
			_openPanel = string.Empty;
			Send(new { type = "close_all" });
		}

		/// <summary>向 Web UI 推送剧情时间线元数据与当前进度。</summary>
		public void SendStoryMeta()
		{
			if (_storyManager == null) return;
			Send(_storyManager.BuildMeta());
		}

		/// <summary>向 Web UI 推送当前剧情进度。</summary>
		public void SendStoryProgress()
		{
			if (_storyManager == null) return;
			Send(_storyManager.BuildProgress());
		}

		private void OnPageLoadFinished(string url)
		{
			// 页面加载完成，等待 JS 的 ready 消息后再推送（桥接脚本注入可能略晚）
		}

		private void FlushPending()
		{
			_pageReady = true;
			foreach (string json in _pendingMessages)
				_webView?.Call("post_message", json);
			_pendingMessages.Clear();
		}

		private void SendInit()
		{
			Send(new
			{
				type = "init",
				cps = _dialogueBox?.CharactersPerSecond ?? 40f,
				auto = _core?.IsAutoMode ?? false
			});
		}

		private void OnIpcMessage(string message)
		{
			JsonDocument doc;
			try
			{
				doc = JsonDocument.Parse(message);
			}
			catch (JsonException)
			{
				GD.PushWarning($"WebUI: 无法解析的 IPC 消息: {message}");
				return;
			}

			using (doc)
			{
				if (!doc.RootElement.TryGetProperty("type", out var typeEl)) return;
				string type = typeEl.GetString();

				switch (type)
				{
					case "ready":
						_openPanel = string.Empty;
						FlushPending();
						SendInit();
						SendStoryMeta();
						// 引擎处于主菜单待命状态时，命令 Web 端显示标题界面
						if (_core != null && !_core.GameStarted)
							SendMainMenu();
						break;
					case "advance":
						if (_core.IsTyping) Send(new { type = "skip_typing" });
						else _core.Advance();
						break;
					case "typing_complete":
						_core.OnDialogueComplete();
						break;
					case "choice":
						_core.OnChoiceMade(doc.RootElement.GetProperty("target").GetString());
						break;
					case "open_panel":
						OpenPanel(doc.RootElement.GetProperty("panel").GetString());
						break;
					case "panel_opened":
						_openPanel = doc.RootElement.GetProperty("panel").GetString() ?? string.Empty;
						break;
					case "panel_closed":
						_openPanel = string.Empty;
						break;
					case "save_slot":
						SaveLoadManager.Save(doc.RootElement.GetProperty("slot").GetInt32(), _core);
						SendSaveSlots("save");
						break;
					case "load_slot":
						OnLoadSlot(doc.RootElement.GetProperty("slot").GetInt32());
						break;
					case "apply_settings":
						ApplySettings(doc.RootElement);
						break;
					case "toggle_auto":
						_core.ToggleAutoMode();
						Send(new { type = "auto_state", on = _core.IsAutoMode });
						break;
					case "skip_start":
						_core.IsSkipReadMode = true;
						break;
					case "skip_end":
						_core.IsSkipReadMode = false;
						break;
					case "menu_new_game":
						// 主菜单「开始游戏」：隐藏菜单，从头开始剧情
						_openPanel = string.Empty;
						Send(new { type = "hide_menu" });
						_core.StartNewGame();
						break;
					case "menu_continue":
						// 主菜单「读取存档」：打开读档面板（覆盖在菜单之上）
						OpenPanel("load");
						break;
					case "menu_settings":
						OpenPanel("settings");
						break;
					case "menu_gallery":
						OpenPanel("gallery");
						break;
					case "menu_quit":
						GetTree().Quit();
						break;
					case "story_seek":
						_storyManager?.Seek(
							doc.RootElement.GetProperty("sceneId").GetString(),
							GetInt(doc.RootElement, "commandIndex", 0));
						break;
					case "story_seek_global":
						_storyManager?.SeekGlobal(GetInt(doc.RootElement, "index", 0));
						break;
				}
			}
		}

		private void OnLoadSlot(int slot)
		{
			var data = SaveLoadManager.Load(slot);
			if (data == null) return;
			CloseAll();
			// 从主菜单读档时，同时隐藏标题界面并进入游戏状态
			Send(new { type = "hide_menu" });
			_core.MarkGameStarted();
			_core.ApplySaveData(data);
		}

		/// <summary>向 Web 端推送主菜单显示指令（附是否有可用存档），并播放标题音乐。</summary>
		private void SendMainMenu()
		{
			bool hasSaves = false;
			for (int i = 0; i < SlotCount; i++)
			{
				if (SaveLoadManager.Load(i) != null)
				{
					hasSaves = true;
					break;
				}
			}
			Audio.AudioManager.Instance?.PlayBgm("bgm_menu", 2.0f, true);
			Send(new { type = "show_menu", hasSaves });
		}

		// ========== 面板数据供给 ==========

		private void SendHistory()
		{
			Send(new
			{
				type = "history",
				entries = _core.State.History.Select(h => new
				{
					speaker = h.SpeakerName,
					text = h.Text
				}).ToArray()
			});
		}

		private void SendSaveSlots(string mode)
		{
			var slots = new List<object>();
			for (int i = 0; i < SlotCount; i++)
			{
				var data = SaveLoadManager.Load(i);
				slots.Add(data == null
					? new { slot = i, empty = true, scene = string.Empty, time = string.Empty }
					: new { slot = i, empty = false, scene = data.CurrentSceneId, time = data.SaveTime.ToString("yyyy-MM-dd HH:mm") });
			}
			Send(new { type = "save_slots", mode, slots });
		}

		private void SendSettings()
		{
			var c = GameConfig.Load();
			Send(new
			{
				type = "settings",
				master = c.MasterVolume,
				bgm = c.BgmVolume,
				se = c.SeVolume,
				voice = c.VoiceVolume,
				fullscreen = c.IsFullscreen,
				fps = c.TargetFps,
				autoDelay = c.AutoDelay,
				skipDelay = c.SkipDelay
			});
		}

		private void SendGallery()
		{
			var unlocks = _core.State.Unlocks;
			Send(new
			{
				type = "gallery",
				cgs = unlocks.Cgs.OrderBy(x => x).ToArray(),
				musics = unlocks.Musics.OrderBy(x => x).ToArray(),
				voices = unlocks.Voices.OrderBy(x => x).ToArray()
			});
		}

		private void ApplySettings(JsonElement root)
		{
			var c = GameConfig.Load();
			c.MasterVolume = GetFloat(root, "master", c.MasterVolume);
			c.BgmVolume = GetFloat(root, "bgm", c.BgmVolume);
			c.SeVolume = GetFloat(root, "se", c.SeVolume);
			c.VoiceVolume = GetFloat(root, "voice", c.VoiceVolume);
			c.IsFullscreen = GetBool(root, "fullscreen", c.IsFullscreen);
			c.TargetFps = GetInt(root, "fps", c.TargetFps);
			c.AutoDelay = GetFloat(root, "autoDelay", c.AutoDelay);
			c.SkipDelay = GetFloat(root, "skipDelay", c.SkipDelay);
			c.Save();

			DisplayServer.WindowSetMode(c.IsFullscreen
				? DisplayServer.WindowMode.Fullscreen
				: DisplayServer.WindowMode.Windowed);
			Godot.Engine.MaxFps = c.TargetFps;
			Audio.AudioManager.Instance?.ApplyVolumes();

			CloseAll();
		}

		private static float GetFloat(JsonElement root, string key, float fallback)
			=> root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Number
				? (float)el.GetDouble() : fallback;

		private static int GetInt(JsonElement root, string key, int fallback)
			=> root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Number
				? el.GetInt32() : fallback;

		private static bool GetBool(JsonElement root, string key, bool fallback)
			=> root.TryGetProperty(key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False
				? el.GetBoolean() : fallback;
	}
}
