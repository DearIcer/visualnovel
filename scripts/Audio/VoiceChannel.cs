using Godot;
using System;

namespace 交互式文本.Audio
{
	/// <summary>
	/// 角色语音轨道，与 BGM/SE 独立混合。
	/// </summary>
	public partial class VoiceChannel : Node
	{
		private AudioStreamPlayer _player;

		public override void _Ready()
		{
			_player = new AudioStreamPlayer { Name = "VoicePlayer" };
			_player.Bus = "Voice";
			AddChild(_player);
		}

		public void Play(string path)
		{
			if (string.IsNullOrEmpty(path)) return;

			if (!ResourceLoader.Exists(path))
			{
				GD.PushWarning($"语音资源未找到: {path}");
				return;
			}

			var stream = GD.Load<AudioStream>(path);
			if (stream == null)
			{
				GD.PushWarning($"语音加载失败: {path}");
				return;
			}

			_player.Stream = stream;
			_player.Play();
		}

		public void Stop()
		{
			if (_player.Playing)
				_player.Stop();
		}

		public bool IsPlaying => _player.Playing;
	}
}
