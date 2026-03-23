using Godot;
using PGEmu.Services;
using PGEmu.Services.Models;
using System;
using System.Threading.Tasks;

public partial class Profile : Control
{
	[Export] public NodePath BackPath;
	[Export] public NodePath AvatarPath;
	[Export] public NodePath ProfileSettingsShortcutPath;
	
	public ProfileService _profileService = new ProfileService();
	private readonly System.Net.Http.HttpClient _client = new();

	private Button _back = null!;
	
	ProfileResponse profile = null!;
	private Label _gamer_tag = null!;
	
	private Label _profile_note = null!;
	
	private Button _friends_list = null!;
	private Button _profileSettingsShortcut = null!;
	
	private TextureRect _avatar;

	public override async void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		if (!_back.IsConnected(Button.SignalName.Pressed, Callable.From(GoBack)))
		{
			_back.Pressed += GoBack;
		}

		_profileSettingsShortcut = GetNode<Button>(ProfileSettingsShortcutPath);
		if (!_profileSettingsShortcut.IsConnected(Button.SignalName.Pressed, Callable.From(GoProfileSettings)))
		{
			_profileSettingsShortcut.Pressed += GoProfileSettings;
		}
		
		_friends_list = GetNode<Button>("Margin/Root/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Button");
		if (!_friends_list.IsConnected(Button.SignalName.Pressed, Callable.From(GoFriendsList)))
		{
			_friends_list.Pressed += GoFriendsList;
		}
		
		_gamer_tag = GetNode<Label>("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/GamerTag");
		_profile_note = GetNode<Label>("Margin/Root/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer/MarginContainer/ProfileNote");
		_avatar = GetNode<TextureRect>(AvatarPath);

		// Keep navigation usable while profile data loads.
		_gamer_tag.Text = "Profile";
		_profile_note.Text = "\"Loading profile...\"";

		ApplyThemeAesthetic();
		await LoadProfileAsync();
	}

	private async Task LoadProfileAsync()
	{
		try
		{
			profile = await _profileService.GetMyProfile();
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
		var title = GetNodeOrNull<Label>("Margin/Root/TopBar/Title");
		UiStyle.StyleTitleLabel(title);

		UiStyle.StyleTitleLabel(_gamer_tag);
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
		StyleButtonList("Margin/Root/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer");
		StyleButtonList("Margin/Root/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer");
		StyleButtonList("Margin/Root/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer");

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
		AudioManager.Instance?.PlayNavigation(-1);
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
	
	private void GoFriendsList() 
	{
		AudioManager.Instance?.PlaySelect();
		var tree = GetTree();
		tree.ChangeSceneToFile("res://FriendsList.tscn");
	}

	private void GoProfileSettings()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://profile.tscn");
		tree.ChangeSceneToFile("res://Settings.tscn");
	}
	
	private async Task LoadAvatar(string url) 
	{
		try
		{
			byte[] imageData = await _client.GetByteArrayAsync(url);
			
			Image avatar = new Image();
			Error err = avatar.LoadPngFromBuffer(imageData);

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !GodotObject.IsInstanceValid(_avatar))
			{
				return;
			}
			
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
