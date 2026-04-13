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

	// Cached scene nodes, resolved in _Ready().
	private Button _back = null!;
	private Button _prev= null!;
	private Button _next = null!;
	public int currentIndex = -1;
	public List<String> results = null;
	private AudioManager audioMan;
	public override void _Ready()
	{
		
		
		// Resolve NodePaths into actual nodes.
		_back = GetNode<Button>(BackPath);
		_prev = GetNode<Button>(PrevPath);
		_next = GetNode<Button>(NextPath);

		VBoxContainer container = GetNode<VBoxContainer>("Margin/Root/Body/ScrollContainer/ButtonContainer");
		GD.Print("hi from after container");

		ApplyThemeAesthetic();
		audioMan = GetNode<AudioManager>("/root/AudioManager");
		if (_back != null) _back.Pressed += GoBack;
		if (_next != null) _next.Pressed += NextSong;
		if (_prev != null) _prev.Pressed += PrevSong;
		results = FindMusic();
		
		
		//int currentIndex = -1;
		for (int i = 0; i < results.Count; i++)
		{
			Button btn = new Button();
			var title = results[i];
			int index = i;
			
			btn.Text = Regex.Replace(title, ".mp3$", "");
			btn.CustomMinimumSize = new Vector2(300, 80);

			UiStyle.StyleTopBarButton(btn);  
			btn.Pressed += () => 
			{
				currentIndex = index;
				audioMan.MusicPlay("res://JukeboxMusic/" + title);
			};
			
			container.AddChild(btn);
		}

		
		foreach (var p in PlatformList.platformList){
			GD.Print(p.Name);
		}
		
	}

	public void NextSong(){
		if (currentIndex != -1){
			currentIndex+=1;
			currentIndex = currentIndex % results.Count;
			audioMan.MusicPlay("res://JukeboxMusic/" + results[currentIndex]);
		}
	}
	public void PrevSong(){
		if (currentIndex != -1){
			currentIndex=currentIndex+results.Count-1;
			currentIndex = currentIndex % results.Count;
			audioMan.MusicPlay("res://JukeboxMusic/" + results[currentIndex]);
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
