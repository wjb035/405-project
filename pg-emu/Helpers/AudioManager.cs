using Godot;
using System.Collections.Generic;

public partial class AudioManager : Node
{
	private const int CarouselSpinVoiceCount = 4;

	public const string ClickSfxPath = "res://audio/fwd.mp3";
	public const string BackSfxPath = "res://audio/bk.mp3";
	public const string ForwardSfxPath = "res://audio/fwd.mp3";
	public const string SelectSfxPath = "res://audio/select2.mp3";
	public const string MessageOpenSfxPath = "res://audio/notif_smooth.mp3";
	public const string AmbientTrackPath = "res://audio/Keygen_1.mp3";
	public const string CarouselSpinSfxPath = "res://audio/spin.mp3";
	public const string CarouselHoverSfxPath = "res://audio/hover.mp3";

	private const float SpinBaseVolumeDb = -6f;
	private const float HoverBaseVolumeDb = -8f;
	private const float HoverFadeOutDb = -40f;
	private const float HoverFadeOutSeconds = 0.12f;

	private AudioStreamPlayer _musicPlayer;
	private AudioStreamPlayer _uiPlayer = null!;
	private AudioStreamPlayer _navigationPlayer = null!;
	private AudioStreamPlayer _hoverPlayer = null!;
	private readonly AudioStreamPlayer[] _spinPlayers = new AudioStreamPlayer[CarouselSpinVoiceCount];
	private int _nextSpinPlayerIndex;
	private Tween? _hoverFadeTween;
	private readonly Dictionary<string, AudioStream> _streamCache = new();

	public static AudioManager Instance { get; private set; } = null!;

	public override void _Ready()
	{
		Instance = this;
		
		
		_musicPlayer = GetNode<AudioStreamPlayer>("Music");
		_musicPlayer.VolumeDb = -100f;
		//AudioStream newTrack = GD.Load<AudioStream>("res://Helpers/Logos.mp3");
		// _musicPlayer.Stream = newTrack;
		
		// loop whatever is currently playing 
		_musicPlayer.Finished += OnMusicFinished;
		//_musicPlayer.Play();
		
		_uiPlayer = GetNode<AudioStreamPlayer>("Ui");
		_navigationPlayer = GetNode<AudioStreamPlayer>("Navigation");
		_hoverPlayer = GetNode<AudioStreamPlayer>("Hover");

		for (int i = 0; i < _spinPlayers.Length; i++)
			_spinPlayers[i] = GetNode<AudioStreamPlayer>($"Spin{i + 1}");
	}
	public bool MusicPlaying(){
		return _musicPlayer.Playing;
	}
	
	public void PauseMusic(){
		 _musicPlayer.StreamPaused = true;
	}
	public void UnpauseMusic(){
		 _musicPlayer.StreamPaused = false;
	}
	public void MusicPlay(string path){
		_musicPlayer = GetNode<AudioStreamPlayer>("Music");
		AudioStream newTrack = GD.Load<AudioStream>(path);
		 _musicPlayer.Stream = newTrack;
		_musicPlayer.Play();
	}
	
	
	private void OnMusicFinished(){
		_musicPlayer.Play();
	}


	private AudioStream? GetStream(string path)
	{
		if (!_streamCache.TryGetValue(path, out var stream))
		{
			stream = GD.Load<AudioStream>(path);
			if (stream == null)
			{
				GD.PrintErr("SFX not found: " + path);
				return null;
			}

			_streamCache[path] = stream;
		}

		return stream;
	}

	private void PlayOnPlayer(AudioStreamPlayer player, string path, float volumeDb = 0f)
	{
		var stream = GetStream(path);
		if (stream == null)
			return;

		player.VolumeDb = volumeDb;
		player.Stream = stream;
		player.Play();
	}

	public void PlayClick() => PlayOnPlayer(_uiPlayer, ClickSfxPath);

	public void PlaySelect() => PlayOnPlayer(_uiPlayer, SelectSfxPath);

	public void PlayMessageOpen() => PlayOnPlayer(_uiPlayer, MessageOpenSfxPath);

	public void PlayCarouselSpin()
	{
		var player = _spinPlayers[_nextSpinPlayerIndex];
		_nextSpinPlayerIndex = (_nextSpinPlayerIndex + 1) % _spinPlayers.Length;
		PlayOnPlayer(player, CarouselSpinSfxPath, SpinBaseVolumeDb);
	}

	public void PlayCarouselHover()
	{
		var stream = GetStream(CarouselHoverSfxPath);
		if (stream == null)
			return;

		_hoverFadeTween?.Kill();
		_hoverFadeTween = null;
		_hoverPlayer.VolumeDb = HoverBaseVolumeDb;
		_hoverPlayer.Stream = stream;
		_hoverPlayer.Play();
		
		_hoverFadeTween = CreateTween();
		_hoverFadeTween.TweenInterval(2.0f);
		_hoverFadeTween.TweenProperty(
			_hoverPlayer,
			"volume_db",
			-20f,  
			0.5f   
		);
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
		PlayOnPlayer(_navigationPlayer, direction < 0 ? BackSfxPath : ForwardSfxPath);
	}
}
