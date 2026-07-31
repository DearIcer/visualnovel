using Godot;
using System;

namespace 交互式文本.Audio
{
	/// <summary>
	/// BGM 双轨道通道，支持播放、停止、交叉淡入淡出。
	/// </summary>
	public partial class MusicChannel : Node
	{
		private AudioStreamPlayer _trackA;
		private AudioStreamPlayer _trackB;
		private AudioStreamPlayer _current;
		private AudioStreamPlayer _previous;

		private string _currentTrack = string.Empty;

		public override void _Ready()
		{
			_trackA = CreatePlayer("TrackA");
			_trackB = CreatePlayer("TrackB");
			_current = _trackA;
		}

		private AudioStreamPlayer CreatePlayer(string name)
		{
			var player = new AudioStreamPlayer { Name = name };
			player.Bus = "BGM";
			AddChild(player);
			return player;
		}

		public void Play(string track, float fade = 1.0f, bool loop = true)
		{
			if (_currentTrack == track && _current.Playing)
				return;

			_previous = _current;
			_current = _current == _trackA ? _trackB : _trackA;
			_currentTrack = track;

			string path = $"res://assets/bgm/{track}.ogg";
			if (!ResourceLoader.Exists(path))
			{
				GD.PushWarning($"BGM 资源未找到: {track}");
				return;
			}

			var stream = GD.Load<AudioStream>(path);
			if (stream == null)
			{
				GD.PushWarning($"BGM 加载失败: {track}");
				return;
			}

			if (stream is AudioStreamOggVorbis ogg)
				ogg.Loop = loop;

			_current.Stream = stream;
			_current.VolumeDb = AudioManager.LinearToDb(0f);
			_current.Play();

			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(_current, "volume_db", AudioManager.LinearToDb(1f), fade);

			if (_previous.Playing)
			{
				tween.TweenProperty(_previous, "volume_db", AudioManager.LinearToDb(0f), fade);
				tween.Chain().TweenCallback(Callable.From(() => _previous.Stop()));
			}
		}

		public void Crossfade(string track, float fade = 1.5f, bool loop = true)
		{
			Play(track, fade, loop);
		}

		public void Stop(float fade = 1.0f)
		{
			if (!_current.Playing) return;

			var tween = CreateTween();
			tween.TweenProperty(_current, "volume_db", AudioManager.LinearToDb(0f), fade);
			tween.Chain().TweenCallback(Callable.From(() => _current.Stop()));
			_currentTrack = string.Empty;
		}

		public AudioStreamPlayer CurrentPlayer => _current;
		public string CurrentTrack => _currentTrack;
	}
}
