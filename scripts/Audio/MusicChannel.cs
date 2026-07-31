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

		private string _currentTrack = string.Empty;
		private bool _currentLoop = true;
		private Tween _fadeTween;

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

		public bool Play(string track, float fade = 1.0f, bool loop = true, float fromPosition = 0f)
		{
			if (string.IsNullOrWhiteSpace(track))
			{
				GD.PushWarning("BGM 轨道名为空");
				return false;
			}

			if (_currentTrack == track && _current.Playing)
				return true;

			string path = $"res://assets/bgm/{track}.ogg";
			if (!ResourceLoader.Exists(path))
			{
				GD.PushWarning($"BGM 资源未找到: {track}");
				return false;
			}

			var stream = GD.Load<AudioStream>(path);
			if (stream == null)
			{
				GD.PushWarning($"BGM 加载失败: {track}");
				return false;
			}

			if (stream is AudioStreamOggVorbis ogg)
				ogg.Loop = loop;

			var previous = _current;
			_current = _current == _trackA ? _trackB : _trackA;
			_currentTrack = track;
			_currentLoop = loop;

			_fadeTween?.Kill();
			_current.Stream = stream;
			_current.VolumeDb = AudioManager.LinearToDb(0f);
			_current.Play(Mathf.Max(0f, fromPosition));

			float duration = Mathf.Max(0f, fade);
			var tween = CreateTween();
			_fadeTween = tween;
			tween.SetParallel(true);
			tween.TweenProperty(_current, "volume_db", AudioManager.LinearToDb(1f), duration);

			if (previous != null && previous.Playing)
			{
				tween.TweenProperty(previous, "volume_db", AudioManager.LinearToDb(0f), duration);
				tween.Chain().TweenCallback(Callable.From(() => previous.Stop()));
			}

			return true;
		}

		public bool Crossfade(string track, float fade = 1.5f, bool loop = true, float fromPosition = 0f)
		{
			return Play(track, fade, loop, fromPosition);
		}

		public void Stop(float fade = 1.0f)
		{
			_fadeTween?.Kill();

			if (!_trackA.Playing && !_trackB.Playing)
			{
				_currentTrack = string.Empty;
				_currentLoop = true;
				return;
			}

			float duration = Mathf.Max(0f, fade);
			if (duration <= 0f)
			{
				_trackA.Stop();
				_trackB.Stop();
				_currentTrack = string.Empty;
				_currentLoop = true;
				return;
			}

			var tween = CreateTween();
			_fadeTween = tween;
			tween.SetParallel(true);
			if (_trackA.Playing)
				tween.TweenProperty(_trackA, "volume_db", AudioManager.LinearToDb(0f), duration);
			if (_trackB.Playing)
				tween.TweenProperty(_trackB, "volume_db", AudioManager.LinearToDb(0f), duration);
			tween.Chain().TweenCallback(Callable.From(() =>
			{
				_trackA.Stop();
				_trackB.Stop();
			}));
			_currentTrack = string.Empty;
			_currentLoop = true;
		}

		public AudioStreamPlayer CurrentPlayer => _current;
		public string CurrentTrack => _currentTrack;
		public bool CurrentLoop => _currentLoop;
		public bool IsPlaying => (_trackA?.Playing ?? false) || (_trackB?.Playing ?? false);
		public float CurrentPosition => _current?.GetPlaybackPosition() ?? 0f;
	}
}
