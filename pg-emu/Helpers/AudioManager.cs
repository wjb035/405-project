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
	public const string CarouselSpinSfxPath = "res://audio/spin.mp3";
	public const string CarouselHoverSfxPath = "res://audio/hover.mp3";
	private const float SpinBaseVolumeDb = -3f;
	private const float HoverBaseVolumeDb = -4f;
	private const float HoverFadeOutDb = -40f;
	private const float HoverFadeOutSeconds = 0.12f;

	private AudioStreamPlayer _sfxPlayer;
	private AudioStreamPlayer _hoverPlayer;
	private Tween? _hoverFadeTween;
	private readonly Dictionary<string, AudioStream> _streamCache = new();
	public static AudioManager Instance { get; private set; }

	public override void _Ready()
	{
		Instance = this;
		_sfxPlayer = GetNode<AudioStreamPlayer>("Sfx");
		_hoverPlayer = GetNode<AudioStreamPlayer>("Hover");
	}

	public void PlaySfx(string path, float volumeDb = 0f)
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

		_sfxPlayer.VolumeDb = volumeDb;
		_sfxPlayer.Stream = stream;
		_sfxPlayer.Play();
	}

	public void PlayClick() => PlaySfx(ClickSfxPath);

	public void PlaySelect() => PlaySfx(SelectSfxPath);

	public void PlayMessageOpen() => PlaySfx(MessageOpenSfxPath);

	public void PlayCarouselSpin() => PlaySfx(CarouselSpinSfxPath, SpinBaseVolumeDb);

	public void PlayCarouselHover()
	{
		if (!_streamCache.TryGetValue(CarouselHoverSfxPath, out var stream))
		{
			stream = GD.Load<AudioStream>(CarouselHoverSfxPath);
			if (stream == null)
			{
				GD.PrintErr("SFX not found: " + CarouselHoverSfxPath);
				return;
			}

			_streamCache[CarouselHoverSfxPath] = stream;
		}

		_hoverFadeTween?.Kill();
		_hoverFadeTween = null;
		_hoverPlayer.VolumeDb = HoverBaseVolumeDb;
		_hoverPlayer.Stream = stream;
		_hoverPlayer.Play();
	}

	public void StopCarouselHover(bool fadeOut = true)
	{
		if (!_hoverPlayer.Playing)
			return;

		_hoverFadeTween?.Kill();
		_hoverFadeTween = null;

		if (!fadeOut)
		{
			_hoverPlayer.Stop();
			_hoverPlayer.VolumeDb = HoverBaseVolumeDb;
			return;
		}

		_hoverFadeTween = CreateTween();
		_hoverFadeTween.TweenProperty(_hoverPlayer, "volume_db", HoverFadeOutDb, HoverFadeOutSeconds);
		_hoverFadeTween.TweenCallback(Callable.From(() =>
		{
			_hoverPlayer.Stop();
			_hoverPlayer.VolumeDb = HoverBaseVolumeDb;
			_hoverFadeTween = null;
		}));
	}

	public void PlayNavigation(int direction)
	{
		PlaySfx(direction < 0 ? BackSfxPath : ForwardSfxPath);
	}
}
