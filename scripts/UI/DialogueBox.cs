using Godot;
using System.Collections.Generic;

namespace 交互式文本
{
	/// <summary>
	/// 对话盒子（Web UI 桥）：保留原公开接口，全部转发给 WebUI 由 HTML/JS 渲染。
	/// </summary>
	public partial class DialogueBox : Control
	{
		/// <summary>打字机速度（字符/秒），页面初始化时传给 Web 端。</summary>
		[Export] public float CharactersPerSecond = 30f;

		/// <summary>显示一条对话。</summary>
		public void ShowDialogue(string speakerName, string text, string nameColorHex)
		{
			ClearChoices();
			WebUI.Instance?.ShowDialogue(speakerName, text, nameColorHex);
		}

		/// <summary>跳过打字机动画，直接显示完整文本。</summary>
		public void SkipTyping()
		{
			WebUI.Instance?.Send(new { type = "skip_typing" });
		}

		/// <summary>显示选择支按钮。</summary>
		public void ShowChoices(List<Engine.ChoiceOption> options)
		{
			WebUI.Instance?.ShowChoices(options);
		}

		/// <summary>清除选择支按钮。</summary>
		public void ClearChoices()
		{
			WebUI.Instance?.Send(new { type = "clear_choices" });
		}
	}
}
