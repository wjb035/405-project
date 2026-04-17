using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;
   
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
	
	// Cached scene nodes, resolved in _Ready().
	private Button _back = null!;
	private Button _prev= null!;
	private Button _next = null!;
	private Button _pause = null!;
	private Button _loop = null!;
	private Button _shuffle = null!;
	private Label _playing = null!;
	
	
	private AudioManager audioMan;
	
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
		
		VBoxContainer container = GetNode<VBoxContainer>("Margin/Root/Body/ScrollContainer/ButtonContainer");
		GD.Print("hi from after container");

		ApplyThemeAesthetic();
		audioMan = GetNode<AudioManager>("/root/AudioManager");
		if (_back != null) _back.Pressed += GoBack;
		if (_next != null) _next.Pressed += Next;
		if (_prev != null) _prev.Pressed += Prev;
		if (_pause != null) _pause.Pressed += Pause;
		if (_loop != null) _loop.Pressed += Loop;
		if (_shuffle != null) _shuffle.Pressed += Shuffle;
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
				_shuffle.Text = "Shuffle";
				_loop.Text = "Loop";
				audioMan.currentIndex = index;
				GD.Print("Pressed " + title);
				_pause.Text = "Pause";
				_playing.Text = "Currently Playing: " + title;
				audioMan.MusicPlay("res://JukeboxMusic/" + title);
			};
			
			container.AddChild(btn);
		}

		
		
		
	}


	public void Shuffle(){
		if (audioMan.musicSetting == "shuffle"){
			_shuffle.Text = "Shuffle";
			audioMan.musicSetting = "stop";
			
			
		}
		else if (audioMan.musicSetting == "stop" ||  audioMan.musicSetting == "loop"){
			_shuffle.Text = "Stop Shuffle";
			// selecting shuffle should take you out of looping a song
			_loop.Text = "Loop";
			
			// we want to fill the list of songs that haven't been played with every index besides
			// the index of the currently playing song (you wouldn't want to move songs and then the next song
			// is the same one)
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
			audioMan.musicSetting = "shuffle";
		}
	} 
	public void Loop(){
		GD.Print(audioMan.musicSetting);
		if (audioMan.musicSetting == "loop"){
			_loop.Text = "Loop";
			audioMan.musicSetting = "stop";
		}
		else if (audioMan.musicSetting == "stop" || audioMan.musicSetting == "shuffle"){
			_loop.Text = "Unloop";
			_shuffle.Text = "Shuffle";
			audioMan.musicSetting = "loop";
		}
		
	}

	public void Pause(){
		if (audioMan.MusicPlaying()){
			GD.Print("music is playing in the hall");
			_pause.Text = "Unpause";
			audioMan.PauseMusic();
		}
		else{
			GD.Print("no music");
			_pause.Text = "Pause";
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
			_loop.Text = "Loop";
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
			_loop.Text = "Loop";
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

		var hint = GetNodeOrNull<Label>("Margin/Root/Body/Hint");
		if (hint != null)
			hint.Text = "Set your library root or your games folder.";
		UiStyle.StyleMetaLabel(hint);
		//UiStyle.StyleMetaButton(_next);

	}

	private void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}

	

	

	
}
