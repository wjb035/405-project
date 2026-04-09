using Godot;
using PGEmu.Services;
using PGEmu.Services.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;


public partial class FoundUserProfile : Control
{
	private const string SettingsParentReturnSceneMeta = "pgemu_profile_settings_parent_return_scene";

	[Export] public NodePath BackPath;
	[Export] public NodePath TitleGamertagPath;
	[Export] public NodePath ProfileStatusPath;
	[Export] public NodePath GamerTagPath;
	[Export] public NodePath ProfileNotePath;
	[Export] public NodePath AvatarPath;
	[Export] public NodePath AddFriendPath;
	[Export] public NodePath DropdownOptionsPath;
	[Export] public NodePath FriendsListPath;
	[Export] public NodePath RecentGamesPath;
	[Export] public NodePath ProfileSettingsShortcutPath;
	
	public ProfileService _profileService = new ProfileService();
	public FriendService _friendService = new FriendService();
	private readonly System.Net.Http.HttpClient _client = new();
	private ProfileResponse profile = Global.foundProfile;
	
	public bool blockedUsers;
	private bool userBlocked = false;
	
	
	private Button _back = null!;
	private Label _gamer_tag = null!;
	private Label _profile_note = null!;
	private Label _title_gamertag = null!;
	private Label _profile_status = null!;
	
	private Button _addFriend = null;
	private Button _friends_list = null!;
	private Button _profileSettingsShortcut = null!;
	private PopupMenu _dropdownOptions = null; 
	
	private TextureRect _avatar;
	

	public override async void _Ready()
	{
		GD.Print("Recieved username:", profile.Username);
		GD.Print(profile.UserId);
		
		_back = GetNode<Button>(BackPath);

			_back.Pressed += GoBack;

		_profileSettingsShortcut = GetNode<Button>(ProfileSettingsShortcutPath);
		if (!_profileSettingsShortcut.IsConnected(Button.SignalName.Pressed, Callable.From(GoProfileSettings)))
		{
			_profileSettingsShortcut.Pressed += GoProfileSettings;
		}
		
		_friends_list = GetNode<Button>(FriendsListPath);
		if (!_friends_list.IsConnected(Button.SignalName.Pressed, Callable.From(GoFriendsList)))
		{
			_friends_list.Pressed += GoFriendsList;
		}
		
		_addFriend = GetNode<Button>(AddFriendPath);
		_addFriend.Pressed += AddFriend;
		
		// Get the blocked users and check if this one is blocked
		GD.Print("rbuhghghh;");
		userBlocked = await _friendService.GetIsBlocked(profile.UserId);
		GD.Print(userBlocked);
		GD.Print("blocekds" + blockedUsers);
		
		
		
		_dropdownOptions = GetNode<MenuButton>(DropdownOptionsPath).GetPopup();
		if (!userBlocked) 
		{
			_dropdownOptions.AddItem("Block", 1);
		} else
		{
			_dropdownOptions.AddItem("Unblock", 1);
		}
		_dropdownOptions.IdPressed += OnDropdownSelected;
		
		_profile_status = GetNode<Label>(ProfileStatusPath);
		_title_gamertag = GetNode<Label>(TitleGamertagPath);
		_gamer_tag = GetNode<Label>(GamerTagPath);
		_profile_note = GetNode<Label>(ProfileNotePath);
		_avatar = GetNode<TextureRect>(AvatarPath);

		// Keep navigation usable while profile data loads.
		_title_gamertag.Text = $"{profile.Username}'s Profile";
		_gamer_tag.Text = profile.Username;
		_profile_note.Text = $"\"{profile.Bio}\"";
		
		ApplyThemeAesthetic();
		LoadProfileAsync();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
	}

	private void LoadProfileAsync()
	{
		try
		{
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
			{
				return;
			}

			if (profile == null)
			{
				if (GodotObject.IsInstanceValid(_profile_note))
				{
					_profile_note.Text = "\"Unable to load profile right now.\"";
				}
				return;
			}

			if (GodotObject.IsInstanceValid(_gamer_tag))
			{
				_gamer_tag.Text = string.IsNullOrWhiteSpace(profile.Username) ? "Player" : profile.Username;
			}

			if (GodotObject.IsInstanceValid(_profile_note))
			{
				_profile_note.Text = string.IsNullOrWhiteSpace(profile.Bio) ? "\"No bio yet.\"" : $"\"{profile.Bio}\"";
			}

			if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
			{
				GD.Print("Avatar: ", profile.AvatarUrl);
				_ = LoadAvatar(profile.AvatarUrl);
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Profile load failed: {exception.Message}");
			if (GodotObject.IsInstanceValid(this) && IsInsideTree() && GodotObject.IsInstanceValid(_profile_note))
			{
				_profile_note.Text = "\"Unable to load profile right now.\"";
			}
		}
	}

	private void ApplyThemeAesthetic()
	{
		//var title = GetNodeOrNull<Label>(_title_gamertag);
		UiStyle.StyleTitleLabel(_title_gamertag);

		UiStyle.StyleTitleLabel(_gamer_tag);
		UiStyle.StyleMetaLabel(_profile_status);
		UiStyle.StyleMetaLabel(_profile_note);

		// Section headers
		UiStyle.StyleTitleLabel(GetNodeOrNull<Label>("Margin/Root/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/Showcase"));
		UiStyle.StyleTitleLabel(GetNodeOrNull<Label>("Margin/Root/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/Label"));
		UiStyle.StyleTitleLabel(GetNodeOrNull<Label>("Margin/Root/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Label"));

		// Themed cards to match the rest of the launcher aesthetic.
		ApplyCardStyle("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer2");
		ApplyCardStyle("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer");
		ApplyCardStyle("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer");
		ApplyCardStyle("Margin/Root/Body/RecentGamesAndFriends/ShowcaseSection");
		ApplyCardStyle("Margin/Root/Body/RecentGamesAndFriends/RecentGames2");
		ApplyCardStyle("Margin/Root/Body/RecentGamesAndFriends/Friends");

		// Style all list buttons consistently.
		UiStyle.StyleTopBarButton(_back);
		UiStyle.StyleTopBarButton(_addFriend);
		UiStyle.StyleTopBarButton(GetNode<MenuButton>(DropdownOptionsPath));
		UiStyle.StylePopupMenu(_dropdownOptions);

		// Center "See All" controls so they read as navigation actions.
		var seeAllRecent = GetNodeOrNull<Button>("Margin/Root/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/Button");
		var seeAllFriends = GetNodeOrNull<Button>("Margin/Root/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Button");
		if (seeAllRecent != null) seeAllRecent.Text = "See All";
		if (seeAllFriends != null) seeAllFriends.Text = "See All";
	}

	private void StyleButtonList(string containerPath)
	{
		var container = GetNodeOrNull<Node>(containerPath);
		if (container == null)
			return;

		foreach (Node child in container.GetChildren())
		{
			if (child is not Button button)
				continue;

			// Keep list rows compact and consistent.
			if (button.CustomMinimumSize.Y >= 56f)
				button.CustomMinimumSize = new Vector2(button.CustomMinimumSize.X, 52f);
		}
	}

	private void ApplyCardStyle(string nodePath)
	{
		var control = GetNodeOrNull<Control>(nodePath);
		if (control == null)
			return;

		control.AddThemeStyleboxOverride("panel", CreateCardStyle());
	}

	private static StyleBoxFlat CreateCardStyle()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.10f, 0.09f, 0.16f, 0.90f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			BorderColor = new Color(0.62f, 0.52f, 0.84f, 0.70f),
			BorderBlend = true,
			CornerRadiusTopLeft = 12,
			CornerRadiusTopRight = 12,
			CornerRadiusBottomRight = 12,
			CornerRadiusBottomLeft = 12
		};
	}

	private void GoBack()
	{
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;

		if (string.Equals(returnScene, "res://profile.tscn", StringComparison.OrdinalIgnoreCase) &&
			tree.HasMeta(SettingsParentReturnSceneMeta))
		{
			var parentScene = tree.GetMeta(SettingsParentReturnSceneMeta).AsString();
			if (!string.IsNullOrWhiteSpace(parentScene))
				returnScene = parentScene;

			tree.RemoveMeta(SettingsParentReturnSceneMeta);
		}

		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;
		tree.SetMeta("pgemu_return_scene", returnScene);

		tree.ChangeSceneToFile(returnScene);
	}
	
	private async void AddFriend()
	{
		_friendService.SendFriendRequest(profile.UserId);
		_addFriend.Hide();
		//_cancelRequest.Show();
		
	}
	
	private void OnDropdownSelected(long id)
	{	
		GD.Print("gungingigngnahgjgidsknfkajhfrekfhrewoi");
		switch (id)
		{
			case 0:
				// messaging needs to be implemented
				break;
			case 1:
				if (_dropdownOptions.GetItemText(1) == "Block")
				{	
					BlockUser();
				}
				else if (_dropdownOptions.GetItemText(1) == "Unblock")
				{
					UnblockUser();
				}
				break;
			case 2:
				
				break;
		}
		
	}
	
	public void BlockUser()
	{
		GD.Print($"Attempting to block {profile.UserId}");
		_friendService.Block(profile.UserId);
		_dropdownOptions.RemoveItem(1);
		_dropdownOptions.AddItem("UnBlock", 1);
	}
	
		public void UnblockUser()
	{
		GD.Print($"Attempting to block {profile.UserId}");
		_friendService.Unblock(profile.UserId);
		_dropdownOptions.RemoveItem(1);
		_dropdownOptions.AddItem("Block", 1);
	}
	
	private void GoFriendsList() 
	{
		var tree = GetTree();
		tree.ChangeSceneToFile("res://FriendsList.tscn");
	}

	private void GoProfileSettings()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.SetMeta(SettingsParentReturnSceneMeta, returnScene);
		tree.SetMeta("pgemu_return_scene", "res://profile.tscn");
		tree.ChangeSceneToFile("res://Settings.tscn");
	}
	
	private async Task LoadAvatar(string url) 
	{
		try
		{
			Image avatar = new Image();
			Error err = Error.Failed;

			var localAvatarPath = TryResolveLocalAvatarPath(url);
			if (!string.IsNullOrWhiteSpace(localAvatarPath) && System.IO.File.Exists(localAvatarPath))
			{
				err = avatar.Load(localAvatarPath);
			}
			else
			{
				if (url.StartsWith("/"))
					url = $"http://localhost:5276{url}";

				byte[] imageData = await _client.GetByteArrayAsync(url);
				err = avatar.LoadPngFromBuffer(imageData);
				if (err != Error.Ok)
					err = avatar.LoadJpgFromBuffer(imageData);
			}

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !GodotObject.IsInstanceValid(_avatar))
			{
				return;
			}
			
			if (err != Error.Ok)
			{
				GD.PrintErr("Failed to decode avatar image");
				return;
			}

			ImageTexture texture = ImageTexture.CreateFromImage(avatar);
			_avatar.Texture = texture;
		}
		catch (System.Exception exception)
		{
			GD.PrintErr("Failed to load image: " + exception.Message);
		}
		
	}

	private static string? TryResolveLocalAvatarPath(string avatarReference)
	{
		if (string.IsNullOrWhiteSpace(avatarReference))
			return null;

		var cleanReference = avatarReference.Split('?', 2)[0];
		if (!cleanReference.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
			return null;

		var projectDir = ProjectSettings.GlobalizePath("res://");
		var relativePath = cleanReference.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar);
		return System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "PGEmu.backend", relativePath));
	}
	
}
