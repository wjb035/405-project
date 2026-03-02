using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;
using PGEmu.Services.Models;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;

public partial class Profile : Control
{
	[Export] public NodePath BackPath;
	[Export] public NodePath AvatarPath;
	
	public ProfileService _profileService = new ProfileService();
	private System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();

	private Button _back = null!;
	
	ProfileResponse profile = null!;
	private Label _gamer_tag = null!;
	
	private Label _profile_note = null!;
	string profileNote = "Yall better turn yo speakers down I dont care if the music too loud I aint yo daddy boy turn yo speakers down";
	
	private Button _friends_list = null!;
	
	private TextureRect _avatar;

	private AppConfig _config = new();          // In-memory config (loaded or default).
	private string? _configPath;                // Base config.json path (shared).
	private string? _localConfigPath;    

	public override async void _Ready()
	{
		profile = await _profileService.GetMyProfile();
		
		_back = GetNode<Button>(BackPath);
		_back.Pressed += GoBack;
		
		_friends_list = GetNode<Button>("Margin/Root/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Button");
		_friends_list.Pressed += GoFriendsList;
		
		_gamer_tag = GetNode<Label>("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/GamerTag");
		_gamer_tag.Text = profile.Username;
		
		_profile_note = GetNode<Label>("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer/MarginContainer/ProfileNote");;
		_profile_note.Text = '"' + profile.Bio + '"';
		
		_avatar = GetNode<TextureRect>(AvatarPath);
		
		GD.Print("Avatar: ", profile.AvatarUrl);
		_ = LoadAvatar(profile.AvatarUrl);
	}

	private void GoBack()
	{
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
	
	private void GoFriendsList() 
	{
		var tree = GetTree();
		tree.ChangeSceneToFile("res://FriendsList.tscn");
	}
	
	private async Task LoadAvatar(string url) 
	{
		try
		{
			byte[] imageData = await _client.GetByteArrayAsync(url);
			
			Image avatar = new Image();
			Error err = avatar.LoadPngFromBuffer(imageData);
			
			if (err == Error.Ok)
			{
				ImageTexture texture = ImageTexture.CreateFromImage(avatar);
				_avatar.Texture = texture;
			}
			else
			{
				avatar.LoadJpgFromBuffer(imageData);
				ImageTexture texture = ImageTexture.CreateFromImage(avatar);
				_avatar.Texture = texture;
			}
		}
		catch (System.Exception exception)
		{
			GD.PrintErr("Failed to load image: " + exception.Message);
		}
		
	}
	
}
