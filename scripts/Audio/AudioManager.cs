using Godot;
using System;

namespace 交互式文本.Audio
{
	/// <summary>
	/// 音频总线：BGM / SE / Voice 的统一入口。
	/// 作为 Autoload 单例使用。
	/// </summary>
	public partial class AudioManager : Node
	{
		public static AudioManager Instance { get; private set; }

		[Export] public int SoundPoolSize = 8;

		public MusicChannel BgmChannel { get; private set; }
		public SoundPool SoundPool { get; private set; }
		public VoiceChannel VoiceChannel { get; private set; }

		public override void _Ready()
		{
			Instance = this;
			EnsureAudioBuses();

			BgmChannel = new MusicChannel();
			AddChild(BgmChannel);

			SoundPool = new SoundPool(SoundPoolSize);
			AddChild(SoundPool);

			VoiceChannel = new VoiceChannel();
			AddChild(VoiceChannel);

			ApplyVolumes();
		}

		private void EnsureAudioBuses()
		{
			string[] buses = { "BGM", "SE", "Voice" };
			foreach (var bus in buses)
			{
				int idx = AudioServer.GetBusIndex(bus);
				if (idx == -1)
				{
					AudioServer.AddBus(AudioServer.BusCount);
					AudioServer.SetBusName(AudioServer.BusCount - 1, bus);
					AudioServer.SetBusSend(AudioServer.BusCount - 1, "Master");
				}
			}
		}

		public void ApplyVolumes()
		{
			var config = Data.GameConfig.Load();
			AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), LinearToDb(config.MasterVolume));
			AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("BGM"), LinearToDb(config.BgmVolume));
			AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("SE"), LinearToDb(config.SeVolume));
			AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Voice"), LinearToDb(config.VoiceVolume));
		}

		public void PlayBgm(string track, float fade = 1.0f, bool loop = true)
		{
			BgmChannel?.Play(track, fade, loop);
		}

		public void CrossfadeBgm(string track, float fade = 1.5f, bool loop = true)
		{
			BgmChannel?.Crossfade(track, fade, loop);
		}

		public void StopBgm(float fade = 1.0f)
		{
			BgmChannel?.Stop(fade);
		}

		public void PlaySe(string sound, float volume = 1.0f, float pitch = 1.0f)
		{
			SoundPool?.Play($"res://assets/se/{sound}.wav", volume, pitch);
		}

		public void PlayVoice(string path)
		{
			VoiceChannel?.Play(path);
		}

		public void StopVoice()
		{
			VoiceChannel?.Stop();
		}

		public static float LinearToDb(float linear)
		{
			return linear <= 0.0001f ? -80f : Mathf.LinearToDb(linear);
		}

		public static float DbToLinear(float db)
		{
			return db <= -80f ? 0f : Mathf.DbToLinear(db);
		}
	}
}
