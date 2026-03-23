using Godot;
using System.Collections.Generic;

public partial class AudioManager : Node
{
	public const string ClickSfxPath = "res://audio/fwd.mp3";
	public const string BackSfxPath = "res://audio/bk.mp3";
	public const string ForwardSfxPath = "res://audio/fwd.mp3";
	public const string SelectSfxPath = "res://audio/select2.mp3";
	public const string MessageOpenSfxPath = "res://audio/notif_smooth.mp3";
	public const string AmbientTrackPath = "res://audio/Keygen_1.mp3";

	private AudioStreamPlayer _sfxPlayer;
	private readonly Dictionary<string, AudioStream> _streamCache = new();
	public static AudioManager Instance { get; private set; }

	public override void _Ready()
	{
		Instance = this;
		_sfxPlayer = GetNode<AudioStreamPlayer>("Sfx");
	}

	public void PlaySfx(string path)
	{
		if (!_streamCache.TryGetValue(path, out var stream))
		{
			stream = GD.Load<AudioStream>(path);
			if (stream == null)
			{
				GD.PrintErr("SFX not found: " + path);
				return;
			}

			_streamCache[path] = stream;
		}

		_sfxPlayer.Stream = stream;
		_sfxPlayer.Play();
	}

	public void PlayClick() => PlaySfx(ClickSfxPath);

	public void PlaySelect() => PlaySfx(SelectSfxPath);

	public void PlayMessageOpen() => PlaySfx(MessageOpenSfxPath);

	public void PlayNavigation(int direction)
	{
		PlaySfx(direction < 0 ? BackSfxPath : ForwardSfxPath);
	}
}
