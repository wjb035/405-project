using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;


public partial class AudioManager : Node
{
	private const int CarouselSpinVoiceCount = 4;

	public const string ClickSfxPath = "res://audio/fwd2.mp3";
	public const string BackSfxPath = "res://audio/bk2.mp3";
	public const string ForwardSfxPath = "res://audio/fwd2.mp3";
	public const string SelectSfxPath = "res://audio/select3.mp3";
	public const string MessageOpenSfxPath = "res://audio/notif_smooth.mp3";
	public const string AmbientTrackPath = "res://audio/Keygen_1.mp3";
	public const string CarouselSpinSfxPath = "res://audio/spin2.mp3";
	public const string CarouselHoverSfxPath = "res://audio/hover.mp3";
	public const string GbaOpenSfxPath = "res://audio/gba_open.mp3";
	public const string GbaCloseSfxPath = "res://audio/gba_close.mp3";
	public const string ButtonHoverSfxPath = "res://audio/hover_btn.mp3";
	public const string FlipSfxPath = "res://audio/flip.mp3";

	private const float SpinBaseVolumeDb = -3f;
	private const float HoverBaseVolumeDb = -9f;
	private const float HoverFadeOutDb = -40f;
	private const float HoverFadeOutSeconds = 0.12f;

	private AudioStreamPlayer _musicPlayer;
	private AudioStreamPlayer _uiPlayer = null!;
	private AudioStreamPlayer _navigationPlayer = null!;
	private AudioStreamPlayer _hoverPlayer = null!;
	private AudioStreamPlayer _consolePlayer = null!;
	private AudioStreamPlayer _buttonHoverPlayer = null!;
	private AudioStreamPlayer _boxPlayer = null!;

	private readonly AudioStreamPlayer[] _spinPlayers = new AudioStreamPlayer[CarouselSpinVoiceCount];
	private int _nextSpinPlayerIndex;
	private Tween? _hoverFadeTween;
	private readonly Dictionary<string, AudioStream> _streamCache = new();

	public static AudioManager Instance { get; private set; } = null!;
	public string musicSetting = "stop";
	public int currentIndex = -1;
	public int shuffleIndex = 0;
	public List<String> results = null;
	public List<int> PlayHistory = new();
	public List<int> Unplayed = new();
	
	//private Jukebox juke;
	public override void _Ready()
	{
		Instance = this;
		// juke = GetNode<Jukebox>("/root/Main/Jukebox");
		
		_musicPlayer = GetNode<AudioStreamPlayer>("Music");
		//_musicPlayer.VolumeDb = -100f;
		//AudioStream newTrack = GD.Load<AudioStream>("res://Helpers/Logos.mp3");
		// _musicPlayer.Stream = newTrack;
		
		// loop whatever is currently playing 
		_musicPlayer.Finished += OnMusicFinished;
		
		
		//_musicPlayer.Play();
		
			_uiPlayer = GetNode<AudioStreamPlayer>("Ui");
			_navigationPlayer = GetNode<AudioStreamPlayer>("Navigation");
			_hoverPlayer = GetNode<AudioStreamPlayer>("Hover");
			_consolePlayer = new AudioStreamPlayer { Name = "Console" };
			_buttonHoverPlayer = new AudioStreamPlayer { Name = "ButtonHover" };
			_boxPlayer = new AudioStreamPlayer { Name = "ButtonHover" };

			AddChild(_consolePlayer);
			AddChild(_buttonHoverPlayer);
			AddChild(_boxPlayer);


			for (int i = 0; i < _spinPlayers.Length; i++)
				_spinPlayers[i] = GetNode<AudioStreamPlayer>($"Spin{i + 1}");
	}
	
	
	
	public void OnMusicFinished(){
		
		if (musicSetting == "loop"){
			_musicPlayer.Play();
		}
		else if (musicSetting == "stop"){
			
		}
		else if (musicSetting == "shuffle"){
		
			/*
			general loop is that when a song is done (and there is no other song after it), we want to get a random integer from the 
			unplayed list, then remove it from the unplayed, add it to the history, then start playing it. if we try to 
			go backwards in the queue (before the first entry) don't! so many edge cases here
			*/
			NextShuffle();
			
			
		}
		
	}
	public void PrevShuffle(){
		// Now, what if we're going BACKWARDS when we're in shuffle mode? 
		// Well, luckily the logic here is simple enough. If it's at the first value of the list 
		// (We're at the song we started shuffling w/), then just play that song again!
		// Otherwise, we just find the song before the current one and play it!
		if (shuffleIndex == 0){
			MusicPlay("res://JukeboxMusic/" + results[currentIndex]);
		}
		else{
			// get the previous song index
			shuffleIndex = shuffleIndex - 1;
			int prevSongIndex = PlayHistory[shuffleIndex];
			if (prevSongIndex != -1){
				currentIndex = prevSongIndex;
				MusicPlay("res://JukeboxMusic/" + results[prevSongIndex]);
			}
		}
		
	}
	
	public void NextShuffle(){
		// have we ran out of songs to play that we haven't? purge the list!
		if (Unplayed.Count == 0){
			for (int i = 0; i < results.Count; i++){
				
				Unplayed.Add(i);
				
			}
		}
		bool isEndOfHistory = (PlayHistory.Count-1 == shuffleIndex);
			GD.Print(isEndOfHistory);
			
			//so, we want to get a new song instead of playing what's next
			if (isEndOfHistory){
				Random rnd = new Random();
				int nextSong = rnd.Next(0,Unplayed.Count);
				// so we have the index of where the index of the song is
				int songIndex = Unplayed[nextSong];
				Unplayed.Remove(songIndex);
				PlayHistory.Add(songIndex);
				shuffleIndex++;
				currentIndex = songIndex;
				MusicPlay("res://JukeboxMusic/" + results[songIndex]);
			}
			else{
				// so it's not end of history- we just want to go to the next song in the history
				
				int nextSongLocation = -1;
				
				//find where the current song's index is in the history, and save it to the temp integer above
				
				nextSongLocation = PlayHistory[shuffleIndex+1];
				shuffleIndex++;
				
				// if it's negative one something has gone horribly awry 
				if (nextSongLocation > -1){
					currentIndex = nextSongLocation;
					MusicPlay("res://JukeboxMusic/" + results[nextSongLocation]);
				}
			}
	}
	public bool MusicPlaying(){
		return _musicPlayer.Playing;
	}
	
	public bool IsPaused()
	{
		return _musicPlayer.StreamPaused;
	}

	public bool IsActuallyPlaying()
	{
		return _musicPlayer.Playing && !_musicPlayer.StreamPaused;
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

	public void PlaySelect() => PlayOnPlayer(_uiPlayer, SelectSfxPath, 6f);

	public void PlayMessageOpen() => PlayOnPlayer(_uiPlayer, MessageOpenSfxPath, volumeDb: -5f);

	public void PlayCarouselSpin(float pitch = 1.0f)
	{
		var player = _spinPlayers[_nextSpinPlayerIndex];
		_nextSpinPlayerIndex = (_nextSpinPlayerIndex + 1) % _spinPlayers.Length;
		player.PitchScale = pitch;
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

	public void PlayConsoleAnimationSfx(string path, float volumeDb = -4f)
	{
		PlayOnPlayer(_consolePlayer, path, volumeDb);
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
		_navigationPlayer.PitchScale = (float)GD.RandRange(0.92, 1.08);
		PlayOnPlayer(_navigationPlayer, direction < 0 ? BackSfxPath : ForwardSfxPath);
	}
	
	public void PlayFlip()
	{
		_boxPlayer.PitchScale = (float)GD.RandRange(0.8, 1.1);
		PlayOnPlayer(_boxPlayer, FlipSfxPath, -10f);
	}
	
	public void PlayButtonHover()
	{
		_buttonHoverPlayer.PitchScale = (float)GD.RandRange(0.92, 1.08);
		PlayOnPlayer(_buttonHoverPlayer, ButtonHoverSfxPath);
	}
	
	
	// For seeking through the music
	public void SeekMusic(float position)
	{
		_musicPlayer.Seek(position);
	}
	
	public float GetMusicPlaybackPosition()
	{
		return _musicPlayer.GetPlaybackPosition();
	}

	public float GetMusicStreamLength()
	{
		return _musicPlayer.Stream != null
			? (float)_musicPlayer.Stream.GetLength()
			: 0f;
	}

}
