using Godot;

namespace 交互式文本
{
	/// <summary>
	/// UI 栈管理器（Web UI 桥）：保留原公开接口，面板实际由 Web 页面渲染，
	/// 面板开关状态由 WebUI 根据 JS 回报维护。
	/// </summary>
	public partial class UIManager : Control
	{
		/// <summary>全局单例。</summary>
		public static UIManager Instance { get; private set; }

		/// <summary>是否有任意 UI 面板打开（打开时阻断剧情推进输入）。</summary>
		public bool IsAnyUIOpen => WebUI.Instance?.IsAnyUIOpen ?? false;

		public override void _Ready()
		{
			Instance = this;
		}

		/// <summary>打开历史记录面板。</summary>
		public void ShowHistory() => WebUI.Instance?.OpenPanel("history");

		/// <summary>打开存档面板。</summary>
		public void ShowSaveMenu() => WebUI.Instance?.OpenPanel("save");

		/// <summary>打开读档面板。</summary>
		public void ShowLoadMenu() => WebUI.Instance?.OpenPanel("load");

		/// <summary>打开设置面板。</summary>
		public void ShowSettings() => WebUI.Instance?.OpenPanel("settings");

		/// <summary>打开鉴赏面板。</summary>
		public void ShowGallery() => WebUI.Instance?.OpenPanel("gallery");

		/// <summary>显示/隐藏剧情调试管理器。</summary>
		public void ToggleStoryManager() => WebUI.Instance?.Send(new { type = "toggle_story_bar" });

		/// <summary>关闭所有面板。</summary>
		public void CloseAll() => WebUI.Instance?.CloseAll();
	}
}
