using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using PGEmu.Helpers;

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
	
	// Cached scene nodes, resolved in _Ready().
	private Button _back = null!;
	private Button _prev= null!;
	private Button _next = null!;
	private Button _pause = null!;
	private Button _loop = null!;
	private Button _shuffle = null!;
	private Label _playing = null!;
	
	private Button _friends;
	private Button _inbox;
	private Button _chat;
	private Button _settings;
	private Button _help;
	
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
		
		_friends = GetNode<Button>(FriendsPath);
		_inbox = GetNode<Button>(InboxPath);
		_chat = GetNode<Button>(ChatPath);
		_settings = GetNode<Button>(SettingsPath);
		_help = GetNode<Button>(HelpPath);

		_pause.Icon = PauseIcon;
		_shuffle.Icon = ShuffleOffIcon;
		_loop.Icon = LoopOffIcon;

		
		VBoxContainer container = GetNode<VBoxContainer>("Margin/Root/Body/Mid1/ScrollContainer/ButtonContainer");
		GD.Print("hi from after container");
		
		

		ApplyThemeAesthetic();
		audioMan = GetNode<AudioManager>("/root/AudioManager");
		if (_back != null) _back.Pressed += GoBack;
		if (_next != null) _next.Pressed += Next;
		if (_prev != null) _prev.Pressed += Prev;
		if (_pause != null) _pause.Pressed += Pause;
		if (_loop != null) _loop.Pressed += Loop;
		if (_shuffle != null) _shuffle.Pressed += Shuffle;
		
		if (_settings != null) _settings.Pressed += OnSettingsPressed;
		if (_friends != null) _friends.Pressed += OnFriendsPressed;
		if (_chat != null) _chat.Pressed += OnChatPressed;
		if (_help != null) _help.Pressed += OnHelpPressed;
		if (_inbox != null) _inbox.Pressed += OnInboxPressed;
		
		audioMan.results = FindMusic();
		
		
		if (audioMan.MusicPlaying()){
			_playing.Text = "Currently Playing: " + audioMan.results[audioMan.currentIndex];
		}
		
		
		//int audioMan.currentIndex = -1;
		for (int i = 0; i < audioMan.results.Count; i++)
		{
			Button btn = new Button();
			var title = audioMan.results[i];
			int index = i;
			
			btn.Text = Regex.Replace(title, ".mp3$", "");
			btn.CustomMinimumSize = new Vector2(300, 80);

			UiStyle.StyleTopBarButton(btn);  
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
				_playing.Text = "Currently Playing: " + title;
				audioMan.MusicPlay("res://JukeboxMusic/" + title);
			};
			
			container.AddChild(btn);
		}

		
		
		
	}


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
				_playing.Text = audioMan.results[audioMan.currentIndex];
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
				_playing.Text = audioMan.results[audioMan.currentIndex];
				
		}
		
	}

	public void NextSongSequentially(){
		if (audioMan.currentIndex != -1){
			audioMan.currentIndex+=1;
			audioMan.currentIndex = audioMan.currentIndex % audioMan.results.Count;
			_playing.Text = "Currently Playing: " + audioMan.results[audioMan.currentIndex];
			audioMan.MusicPlay("res://JukeboxMusic/" + audioMan.results[audioMan.currentIndex]);
		}
	}
	public void PrevSongSequentially(){
		if (audioMan.currentIndex != -1){
			audioMan.currentIndex=audioMan.currentIndex+audioMan.results.Count-1;
			audioMan.currentIndex = audioMan.currentIndex % audioMan.results.Count;
			_playing.Text = "Currently Playing: " + audioMan.results[audioMan.currentIndex];
			audioMan.MusicPlay("res://JukeboxMusic/" + audioMan.results[audioMan.currentIndex]);
		}
	}

	public List<String> FindMusic(){
		
		List<String> result = new();
		using var dir = DirAccess.Open("res://JukeboxMusic/");
		if (dir != null)
	{
		dir.ListDirBegin();
		string fileName = dir.GetNext();
		 
		while (fileName != "")
		{
			
			if (Regex.IsMatch(fileName, @"^.*\.mp3$", RegexOptions.IgnoreCase)){
				//GD.Print($"Found file: {fileName}");
				result.Add(fileName);
			}
			
			
			fileName = dir.GetNext();
		}
	}
		return result;
	}



	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
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
		UiStyle.AddHoverFeedback(_prev);
		UiStyle.ApplyParallaxShadow(_prev, offsetY: 5f);
		
		UiStyle.StylePrimaryButton(_next);
		UiStyle.AddHoverFeedback(_next);
		UiStyle.ApplyParallaxShadow(_next, offsetY: 5f);
		
		UiStyle.StylePrimaryButton(_pause);
		UiStyle.AddHoverFeedback(_pause);
		UiStyle.ApplyParallaxShadow(_pause, offsetY: 5f);

		UiStyle.StylePrimaryButton(_loop);
		UiStyle.AddHoverFeedback(_loop);
		UiStyle.ApplyParallaxShadow(_loop, offsetY: 5f);
		
		UiStyle.StylePrimaryButton(_shuffle);
		UiStyle.AddHoverFeedback(_shuffle);
		UiStyle.ApplyParallaxShadow(_shuffle, offsetY: 5f);
		
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
		await Transition.ChangeScene("res://Settings.tscn", ScreenTransition.TransitionType.Wipe, 0.5f, 0.15f, true);
	}
	
	private async void OnFriendsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		await Transition.ChangeScene("res://profile.tscn", ScreenTransition.TransitionType.Wipe, 0.5f, 0.15f);

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
		GD.Print("Help pressed");
	}
	
	private async void GoBack()
	{
		CollectionStorage.currentCollection = null;
		AudioManager.Instance?.PlayNavigation(-1);
		// Navigate back to the home screen scene.
		await Transition.ChangeScene("res://HomeScreen.tscn", ScreenTransition.TransitionType.Wipe, 0.5f, 0.15f);

	}

	

	

	
}
