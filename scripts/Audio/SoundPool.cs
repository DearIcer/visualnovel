using Godot;
using System;
using System.Collections.Generic;

namespace 交互式文本.Audio
{
	/// <summary>
	/// SE 对象池：避免同时播放大量短音频导致爆音或创建开销。
	/// </summary>
	public partial class SoundPool : Node
	{
		private readonly int _capacity;
		private readonly List<AudioStreamPlayer> _players = new();
		private readonly Queue<AudioStreamPlayer> _available = new();

		public SoundPool(int capacity = 8)
		{
			_capacity = capacity;
		}

		public override void _Ready()
		{
			for (int i = 0; i < _capacity; i++)
			{
				var player = new AudioStreamPlayer { Name = $"SePlayer{i}" };
				player.Bus = "SE";
				player.Finished += () => OnPlayerFinished(player);
				AddChild(player);
				_players.Add(player);
				_available.Enqueue(player);
			}
		}

		public void Play(string path, float volume = 1.0f, float pitch = 1.0f)
		{
			if (!ResourceLoader.Exists(path))
			{
				GD.PushWarning($"SE 资源未找到: {path}");
				return;
			}

			var stream = GD.Load<AudioStream>(path);
			if (stream == null)
			{
				GD.PushWarning($"SE 加载失败: {path}");
				return;
			}

			AudioStreamPlayer player;
			if (_available.Count > 0)
			{
				player = _available.Dequeue();
			}
			else
			{
				// 池满时复用最早结束的（当前未播放的任意一个）
				player = _players.Find(p => !p.Playing) ?? _players[0];
			}

			player.Stream = stream;
			player.PitchScale = pitch;
			player.VolumeDb = AudioManager.LinearToDb(volume);
			player.Play();
		}

		private void OnPlayerFinished(AudioStreamPlayer player)
		{
			if (!_available.Contains(player))
				_available.Enqueue(player);
		}
	}
}
