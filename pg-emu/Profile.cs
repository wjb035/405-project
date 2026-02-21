using Godot;
using System;

public partial class Profile : Control
{
	[Export] public NodePath BackPath;

	private Button _back = null!;
	
	string username = "SouljaBoyTellEm";
	private Label _gamer_tag = null!;
	
	private Label _profile_note = null!;
	string profileNote = "Yall better turn yo speakers down I dont care if the music too loud I aint yo daddy boy turn yo speakers down";
	
	private Button _friends_list = null!;

	public override void _Ready()
	{
		_back = GetNode<Button>("Margin/Root/TopBar/Panel/BtnBack");
		_back.Pressed += GoBack;
		
		_friends_list = GetNode<Button>("Margin/Root/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Button");
		_friends_list.Pressed += GoFriendsList;
		
		_gamer_tag = GetNode<Label>("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/GamerTag");
		_gamer_tag.Text = username;
		
		_profile_note = GetNode<Label>("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer/MarginContainer/ProfileNote");;
		_profile_note.Text = '"' + profileNote + '"';
		
	}

	private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
	
	private void GoFriendsList() {
		var tree = GetTree();
		tree.ChangeSceneToFile("res://FriendsList.tscn");
	}
}
