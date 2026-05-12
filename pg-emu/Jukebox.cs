using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using PGEmu.Helpers;
using PGEmu.UI;

public partial class Jukebox : Control
{  
	private const string LibraryRefreshTokenMeta = "pgemu_library_refresh_token";

	// NodePaths assigned in vault.tscn, keeps UI wiring in-editor instead of hardcoding node strings.
	[Export] public NodePath BackPath;
	[Export] public NodePath PrevPath;
	[Export] public NodePath NextPath;
	[Export] public NodePath PausePath;
	[Export] public NodePath LoopPath;
	[Export] public NodePath ShufflePath;
	[Export] public NodePath PlayingPath;
	
	// Progressbar
	[Export] public NodePath ProgressPath;
	[Export] public NodePath CurrentTimePath;
	[Export] public NodePath TotalTimePath;
	
	// Topbar
	[Export] public NodePath FriendsPath;
	[Export] public NodePath InboxPath;
	[Export] public NodePath ChatPath;
	[Export] public NodePath SettingsPath;
	[Export] public NodePath HelpPath;
	
	[Export] public Texture2D PauseIcon;
	[Export] public Texture2D PlayIcon;
	
	[Export] public Texture2D LoopOnIcon;
	[Export] public Texture2D LoopOffIcon;
	[Export] public Texture2D ShuffleOnIcon;
	[Export] public Texture2D ShuffleOffIcon;
	
	// Volume
	[Export] public NodePath VolumeButtonPath;
	[Export] public NodePath VolumePath;
	[Export] public Texture2D MuteIcon;
	[Export] public Texture2D UnmuteIcon;
	
	[Export] public NodePath ImportButtonPath;
	
	// Cached scene nodes, resolved in _Ready().
	private Button _back = null!;
	private Button _prev= null!;
	private Button _next = null!;
	private Button _pause = null!;
	private Button _loop = null!;
	private Button _shuffle = null!;
	private Label _playing = null!;
	
	// Progress bar
	private HSlider _progressBar;
	private Label _currentTimeLabel;
	private Label _totalTimeLabel;
	private bool _isScrubbing = false;
	
	private Button _friends;
	private Button _inbox;
	private Button _chat;
	private Button _settings;
	private Button _help;
	private HelpPopup _helpPopup = null!;
	
	private HSlider _volumeSlider;
	private Button _volumeButton;
	private float _preMuteVolume = 1f;
	private bool _isMuted = false;
	
	private FileDialog _importDialog;
	private Button _importButton;
	
	private ScreenTransition Transition =>
		GetNode<ScreenTransition>("/root/ScreenTransition");
	
	private AppConfig? _config;
	private string? _configPath;
	
	private AudioManager audioMan;
	
	[Export] public FriendInbox FriendInboxPopup;

	
	/*
		GENERAL TO DO LIST FOR MYSELF
		-----------------------------------------------
		
		
		
	
		
		MUSIC SHOULD PAUSE WHEN A GAME IS LAUNCHED
		
		
		
		
	*/
	
	
	
	public override void _Ready()
	{
		
		// Resolve NodePaths into actual nodes.
		_back = GetNode<Button>(BackPath);
		_prev = GetNode<Button>(PrevPath);
		_next = GetNode<Button>(NextPath);
		_pause = GetNode<Button>(PausePath);
		_loop = GetNode<Button>(LoopPath);
		_shuffle = GetNode<Button>(ShufflePath);
		_playing = GetNode<Label>(PlayingPath);
		
		_progressBar = GetNode<HSlider>(ProgressPath);
		_currentTimeLabel = GetNode<Label>(CurrentTimePath);
		_totalTimeLabel = GetNode<Label>(TotalTimePath);
		_volumeSlider = GetNode<HSlider>(VolumePath);
		_volumeButton = GetNode<Button>(VolumeButtonPath);
		_friends = GetNode<Button>(FriendsPath);
		_inbox = GetNode<Button>(InboxPath);
		
		var chatManager = GetNode<ChatManager>("/root/ChatManager");
		FriendInboxPopup.AttachBadgeButton(_inbox);
		chatManager.LoadUnreadCounts();
		_chat = GetNode<Button>(ChatPath);
		_settings = GetNode<Button>(SettingsPath);
		_help = GetNode<Button>(HelpPath);
		SetupHelpPopup();
		
		_importButton = GetNode<Button>(ImportButtonPath);

		_pause.Icon = PauseIcon;
		_shuffle.Icon = ShuffleOffIcon;
		_loop.Icon = LoopOffIcon;

		
		VBoxContainer container = GetNode<VBoxContainer>("Margin/Root/Body/Mid1/ScrollContainer/ButtonContainer");
		GD.Print("hi from after container");

		StartBackgroundTransition();

		ApplyThemeAesthetic();
		audioMan = GetNode<AudioManager>("/root/AudioManager");
		if (_back != null) _back.Pressed += GoBack;
		if (_next != null) _next.Pressed += Next;
		if (_prev != null) _prev.Pressed += Prev;
		if (_pause != null) _pause.Pressed += Pause;
		if (_loop != null) _loop.Pressed += Loop;
		if (_shuffle != null) _shuffle.Pressed += Shuffle;
		if (_volumeButton != null) _volumeButton.Pressed += Mute;
		
		if (_settings != null) _settings.Pressed += OnSettingsPressed;
		if (_friends != null) _friends.Pressed += OnFriendsPressed;
		if (_chat != null) _chat.Pressed += OnChatPressed;
		if (_help != null) _help.Pressed += OnHelpPressed;
		if (_inbox != null) _inbox.Pressed += OnInboxPressed;
		
		// Volume slider stuff
		_volumeSlider.MinValue = 0.0;
		_volumeSlider.MaxValue = 1.0;
		_volumeSlider.Step = 0.01;
		_volumeSlider.Value = audioMan.GetMusicVolume();
		_volumeSlider.ValueChanged += (value) =>
		{
			if (_isMuted && value > 0.001)
			{
				_isMuted = false;
				_volumeButton.Icon = UnmuteIcon;
			}
			audioMan.SetMusicVolume((float)value);
		};
		
		audioMan.results = FindMusic();
		
		_playing.Text = audioMan.MusicPlaying()
			? FormatTrackName(audioMan.results[audioMan.currentIndex])
			: "Nothing Playing";
		
		// Progress bar intiialization
		_progressBar.MinValue = 0;
		_progressBar.Step = 0.01;
		
		_progressBar.DragStarted += () => _isScrubbing = true;
		
		_progressBar.DragEnded += (changed) =>
		{
			_isScrubbing = false;
			if (changed)
				audioMan.SeekMusic((float)_progressBar.Value);
		};
		
		// Song importing
		_importButton.Pressed += OpenImportDialog;
		_importDialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			Title = "Import Song",
			Filters = new string[] { "*.mp3 ; MP3 Files" },
			Size = new Vector2I(800, 500),
		};
		_importDialog.FileSelected += OnSongImported;
		AddChild(_importDialog);
		
		RebuildSongList();
	}
	
	// Replaced the ready building with a rebuild method
	private void RebuildSongList()
	{
		var container = GetNode<VBoxContainer>("Margin/Root/Body/Mid1/ScrollContainer/ButtonContainer");

		// Clear existing buttons and then rebuild
		foreach (Node child in container.GetChildren())
			child.QueueFree();
		
		//int audioMan.currentIndex = -1;
		for (int i = 0; i < audioMan.results.Count; i++)
		{
			Button btn = new Button();
			var title = audioMan.results[i];
			int index = i;
			
			
			btn.Text = Regex.Replace(System.IO.Path.GetFileName(title), @"\.mp3$", "", RegexOptions.IgnoreCase);
			btn.CustomMinimumSize = new Vector2(300, 80);
			btn.Alignment = HorizontalAlignment.Left;
			btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			btn.AddThemeFontSizeOverride("font_size", 18);
			btn.AddThemeFontOverride("font", GD.Load<FontFile>("res://Fonts/DM_Mono/DMMono-Medium.ttf"));
			
			var normal = UiStyle.CreateButtonStyle(
				new Color(0.35f, 0.15f, 0.30f, 0.45f), 
				new Color(0.8f, 0.4f, 0.7f),     
				2, 16f
			);

			var hover = UiStyle.CreateButtonStyle(
				new Color(0.45f, 0.18f, 0.38f, 0.75f),
				new Color(1.0f, 0.5f, 0.65f),
				2, 16f
			);

			var pressed = UiStyle.CreateButtonStyle(
				new Color(0.25f, 0.10f, 0.22f, 0.45f),
				new Color(0.7f, 0.3f, 0.7f),
				2, 16f
			);

			
			btn.AddThemeStyleboxOverride("normal", normal);
			btn.AddThemeStyleboxOverride("hover", hover);
			btn.AddThemeStyleboxOverride("pressed", pressed);
			btn.AddThemeStyleboxOverride("focus", normal);
			UiStyle.ApplyParallaxShadow(btn, offsetY: 5f);
			UiStyle.AddHoverFeedback(btn);
			btn.Pressed += () => 
			{
				
				// if someone starts playing a song and they were shuffling, they are no longer shuffling
				audioMan.shuffleIndex = 0;
				audioMan.PlayHistory.Clear();
				audioMan.Unplayed.Clear();
				audioMan.musicSetting = "stop";
				audioMan.currentIndex = index;
				GD.Print("Pressed " + title);
				_pause.Icon = PauseIcon;
				_playing.Text = FormatTrackName(title);
				audioMan.MusicPlay(title);			
			};
			
			container.AddChild(btn);
		}

	}

	public override void _Process(double delta)
	{
		if (!audioMan.MusicPlaying() || _progressBar == null) return;
		
		var length = audioMan.GetMusicStreamLength();
		var position = audioMan.GetMusicPlaybackPosition();
		
		   
		if (length <= 0) return;
	
		_progressBar.MaxValue = length;
	
		if (!_isScrubbing)
			_progressBar.Value = position;
	
		_currentTimeLabel.Text = FormatDuration(position);
		_totalTimeLabel.Text = FormatDuration(length);
	}
	
	private string FormatDuration(float seconds)
	{
		var t = TimeSpan.FromSeconds(seconds);
		return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
	}
	
	private string FormatTrackName(string path) =>
		System.IO.Path.GetFileNameWithoutExtension(path);

	public void Shuffle(){
		if (audioMan.musicSetting == "shuffle"){
			audioMan.musicSetting = "stop";
		}
		else
		{
			// selecting shuffle should take you out of looping a song
			// we want to fill the list of songs that haven't been played with every index besides
			// the index of the currently playing song (you wouldn't want to move songs and then the next song
			// is the same one)
			
			audioMan.musicSetting = "shuffle";
			
			audioMan.Unplayed.Clear();
			audioMan.PlayHistory.Clear();
			
			for (int i = 0; i < audioMan.results.Count; i++){
				if (i == audioMan.currentIndex){
					audioMan.PlayHistory.Add(i);
				}
				else{
					audioMan.Unplayed.Add(i);
				}
			}
			
			foreach (int i in audioMan.Unplayed){
				GD.Print(audioMan.results[i] + " is unplayed");
			}
			
		}
		UpdateModeIcons();
	} 
	public void Loop(){
		GD.Print(audioMan.musicSetting);
		if (audioMan.musicSetting == "loop")
		{
			audioMan.musicSetting = "stop";
		}
		else{
			audioMan.musicSetting = "loop";
		}
		
		UpdateModeIcons();
	}

	private void UpdateModeIcons()
	{
		_shuffle.Icon = audioMan.musicSetting == "shuffle"
			? ShuffleOnIcon
			: ShuffleOffIcon;
		
		_loop.Icon = audioMan.musicSetting == "loop"
			? LoopOnIcon
			: LoopOffIcon;
	}
	
	public void Pause(){
		if (audioMan.IsActuallyPlaying()){
			GD.Print("music is playing in the hall");
			_pause.Icon = PlayIcon;
			audioMan.PauseMusic();
		}
		else{
			GD.Print("no music");
			_pause.Icon = PauseIcon;
			audioMan.UnpauseMusic();
			audioMan.SeekMusic((float)_progressBar.Value);
		}
	}
	
	
	public void Next(){
		if (audioMan.musicSetting == "stop"){
			NextSongSequentially();
		}
		else if (audioMan.musicSetting == "loop"){
			NextSongSequentially();
			audioMan.musicSetting = "stop";
		}
		else if (audioMan.musicSetting == "shuffle"){
				audioMan.NextShuffle();
				_playing.Text = FormatTrackName(audioMan.results[audioMan.currentIndex]);
		}
	}
	public void Prev(){
		if (audioMan.musicSetting == "stop"){
			PrevSongSequentially();
		}
		else if (audioMan.musicSetting == "loop"){
			PrevSongSequentially();
			audioMan.musicSetting = "stop";
		}
		else if (audioMan.musicSetting == "shuffle"){
				audioMan.PrevShuffle();
				_playing.Text = FormatTrackName(audioMan.results[audioMan.currentIndex]);				
		}
		
	}

	public void NextSongSequentially(){
		if (audioMan.currentIndex != -1){
			audioMan.currentIndex+=1;
			audioMan.currentIndex = audioMan.currentIndex % audioMan.results.Count;
			_playing.Text = FormatTrackName(audioMan.results[audioMan.currentIndex]);
			audioMan.MusicPlay(audioMan.results[audioMan.currentIndex]);
			
		}
	}
	public void PrevSongSequentially(){
		if (audioMan.currentIndex != -1){
			audioMan.currentIndex=audioMan.currentIndex+audioMan.results.Count-1;
			audioMan.currentIndex = audioMan.currentIndex % audioMan.results.Count;
			_playing.Text = FormatTrackName(audioMan.results[audioMan.currentIndex]);
			audioMan.MusicPlay(audioMan.results[audioMan.currentIndex]);
			
		}
	}

	public List<String> FindMusic()
	{

		var result = new List<string>();
		ScanMusicDir("res://JukeboxMusic/", result);
		ScanMusicDir("user://JukeboxMusic/", result);
		return result;
	}
	private void ScanMusicDir(string godotPath, List<string> result)
	{
		using var dir = DirAccess.Open(godotPath);
		if (dir == null) return;

		dir.ListDirBegin();
		string fileName = dir.GetNext();
		while (fileName != "")
		{
			if (Regex.IsMatch(fileName, @"^.*\.mp3$", RegexOptions.IgnoreCase))
				result.Add(godotPath + fileName); // full path
			fileName = dir.GetNext();
		}
	}

	public void Mute()
	{
		if (_isMuted)
		{
			audioMan.SetMusicVolume(_preMuteVolume);
			_volumeSlider.Value = _preMuteVolume;
			_isMuted = false;
			_volumeButton.Icon = UnmuteIcon;
		}
		else
		{
			_preMuteVolume = (float)_volumeSlider.Value;
			audioMan.SetMusicVolume(0f);
			_volumeSlider.Value = 0f;
			_isMuted = true;
			_volumeButton.Icon = MuteIcon;
		}
	}
	
	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
	}
	
	// Import an mp3
	private void OpenImportDialog()
	{
		_importDialog.PopupCentered();
	}
	
	private void OnSongImported(string path)
	{
		var fileName = System.IO.Path.GetFileName(path);
		var userMusicDir = "user://JukeboxMusic/";
		var absoluteDir = ProjectSettings.GlobalizePath(userMusicDir);

		if (!System.IO.Directory.Exists(absoluteDir))
			System.IO.Directory.CreateDirectory(absoluteDir);

		var absoluteDest = System.IO.Path.Combine(absoluteDir, fileName);
		if (!System.IO.File.Exists(absoluteDest))
			System.IO.File.Copy(path, absoluteDest);

		audioMan.results = FindMusic();
		RebuildSongList();
		
		GD.Print($"Imported: {fileName}");
	}

	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var hint = GetNodeOrNull<Label>("Margin/Root/Body/Mid1/Hint");
		if (hint != null)
			hint.Text = "Set your library root or your games folder.";
		UiStyle.StyleMetaLabel(hint);
		
		UiStyle.StylePrimaryButton(_prev);
		UiStyle.ApplyParallaxShadow(_prev, offsetY: 5f);
		
		UiStyle.StylePrimaryButton(_next);
		UiStyle.ApplyParallaxShadow(_next, offsetY: 5f);
		
		UiStyle.StylePrimaryButton(_pause);
		UiStyle.ApplyParallaxShadow(_pause, offsetY: 5f);

		UiStyle.StylePrimaryButton(_loop);
		UiStyle.ApplyParallaxShadow(_loop, offsetY: 5f);
		
		UiStyle.StylePrimaryButton(_shuffle);
		UiStyle.ApplyParallaxShadow(_shuffle, offsetY: 5f);
		
		UiStyle.StylePrimaryButton(_volumeButton);
		UiStyle.ApplyParallaxShadow(_volumeButton, offsetY: 5f);
		
		UiStyle.StyleGhostNav(1f,_prev, _next, _pause, _loop, _shuffle, _volumeButton);
		
		UiStyle.StylePrimaryButton(_importButton);
		UiStyle.AddHoverFeedback(_importButton);
		UiStyle.ApplyParallaxShadow(_importButton);
		
		UiStyle.StyleTopBarButton(_back);
		UiStyle.AddHoverFeedback(_back);
		UiStyle.ApplyParallaxShadow(_back);
		
		UiStyle.StyleTopBarButton(_inbox);
		UiStyle.AddHoverFeedback(_inbox);
		UiStyle.ApplyParallaxShadow(_inbox);

		UiStyle.StyleTopBarButton(_friends);
		UiStyle.AddHoverFeedback(_friends);
		UiStyle.ApplyParallaxShadow(_friends);

		UiStyle.StyleTopBarButton(_chat);
		UiStyle.AddHoverFeedback(_chat);
		UiStyle.ApplyParallaxShadow(_chat);

		UiStyle.StyleTopBarButton(_settings);
		UiStyle.AddHoverFeedback(_settings);
		UiStyle.ApplyParallaxShadow(_settings);

		UiStyle.StyleTopBarButton(_help);
		UiStyle.AddHoverFeedback(_help);
		UiStyle.ApplyParallaxShadow(_help);

	}

	private void OnAnyButtonPressed()
	{
		var audio = GetNode<AudioManager>("/root/AudioManager");
		audio.PlayClick();
	}
	
	private async void OnSettingsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		// Jump to the shared settings screen and return here afterward.
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://Jukebox.tscn");
		tree.SetMeta("pgemu_settings_tab", "appearance");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);
		await Transition.ChangeScene("res://Settings.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
	}
	
	private async void OnFriendsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		await Transition.ChangeScene("res://profile.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);

	}
	
	private void OnInboxPressed()
	{
		FriendInboxPopup.ShowPopup();
	}

	
	private void OnChatPressed()
	{
		GD.Print("Chat pressed");
	}

	private void OnHelpPressed()
	{
		_helpPopup.ShowPopup();
	}

	private void SetupHelpPopup()
	{
		_helpPopup = new HelpPopup();
		AddChild(_helpPopup);
	}
	
	private async void GoBack()
	{
		CollectionStorage.currentCollection = null;
		AudioManager.Instance?.PlayNavigation(-1);
		// Navigate back to the home screen scene.
		await Transition.ChangeScene("res://HomeScreen.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);

	}
	
	// BACKGOURND STUFF
	private void StartBackgroundTransition()
	{
		var bg = GetNode<GlobalBackground>("/root/GlobalBackground");
		if (bg == null)
		{
			GD.PrintErr("GlobalBackground node not found!");
			return;
		}
		try
		{
			bg.StartTransition("JukeboxScreen", 1.5f);
			GD.Print("Background transition finished!");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Gradient transition failed: {ex.Message}");
		}
		
	}

	

	

	
}
