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
	private MenuButton _dropdownButton = null!;
	private PopupMenu _dropdownOptions = null; 
	
	private TextureRect _avatar;
	private int _uiHorizontalAxisDir;
	private long _uiHorizontalAxisNextMs;
	private int _uiVerticalAxisDir;
	private long _uiVerticalAxisNextMs;
	private int _uiRowIndex = -1;
	private int _uiColumnIndex = -1;
	

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
		
		
		
		_dropdownButton = GetNode<MenuButton>(DropdownOptionsPath);
		_dropdownOptions = _dropdownButton.GetPopup();
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
		CallDeferred(nameof(RefreshControllerFocusGraph));
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (ControllerService.TryHandleBackAction(@event, GoBack))
		{
			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (@event is InputEventJoypadMotion joypadMotion)
		{
			if (!ShouldHandleControllerInput(joypadMotion.Device))
				return;

			if (!HandleControllerUiAxis(joypadMotion))
				return;

			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (@event is not InputEventJoypadButton joypadButton || !joypadButton.Pressed)
			return;

		if (!ShouldHandleControllerInput(joypadButton.Device))
			return;

		if (!HandleControllerUiButton(joypadButton.ButtonIndex))
			return;

		GetViewport()?.SetInputAsHandled();
	}

	private bool HandleControllerUiButton(JoyButton button)
	{
		var rows = GetControllerUiRows();
		if (rows.Count == 0)
			return false;

		if (!IsUiNavigationActive())
		{
			switch (button)
			{
				case JoyButton.DpadUp:
					_uiRowIndex = 0;
					_uiColumnIndex = 0;
					return ControllerService.FocusRowEntry(rows, ref _uiRowIndex, ref _uiColumnIndex);
				case JoyButton.DpadDown:
					_uiRowIndex = rows.Count - 1;
					_uiColumnIndex = 0;
					return ControllerService.FocusRowEntry(rows, ref _uiRowIndex, ref _uiColumnIndex);
				default:
					return false;
			}
		}

		switch (button)
		{
			case JoyButton.DpadLeft:
				return ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, 0, -1);
			case JoyButton.DpadRight:
				return ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, 0, 1);
			case JoyButton.DpadUp:
				return ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, -1, 0);
			case JoyButton.DpadDown:
				return ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, 1, 0);
			default:
				return ControllerService.IsConfirmButton(button) &&
					ControllerService.ActivateRowSelection(rows, _uiRowIndex, _uiColumnIndex);
		}
	}

	private bool HandleControllerUiAxis(InputEventJoypadMotion joypadMotion)
	{
		var rows = GetControllerUiRows();
		if (rows.Count == 0)
			return false;

		if (joypadMotion.Axis == JoyAxis.LeftY)
		{
			if (!IsUiNavigationActive())
			{
				return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs, dir =>
				{
					_uiRowIndex = dir < 0 ? 0 : rows.Count - 1;
					_uiColumnIndex = 0;
					ControllerService.FocusRowEntry(rows, ref _uiRowIndex, ref _uiColumnIndex);
				});
			}

			return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs, dir =>
			{
				ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, dir, 0);
			});
		}

		if (!IsUiNavigationActive() || joypadMotion.Axis != JoyAxis.LeftX)
			return false;

		return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _uiHorizontalAxisDir, ref _uiHorizontalAxisNextMs, dir =>
		{
			ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, 0, dir);
		});
	}

	private List<List<Button>> GetControllerUiRows()
	{
		return ControllerService.BuildVisibleRows(
			new Button?[] { _back, _profileSettingsShortcut },
			new Button?[] { _addFriend, _dropdownButton },
			new Button?[] { _friends_list });
	}

	private bool IsUiNavigationActive()
	{
		return _uiRowIndex >= 0 && _uiColumnIndex >= 0;
	}

	private void ResetUiNavigationState()
	{
		_uiRowIndex = -1;
		_uiColumnIndex = -1;
		ControllerService.ResetMenuAxis(ref _uiHorizontalAxisDir, ref _uiHorizontalAxisNextMs);
		ControllerService.ResetMenuAxis(ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs);
	}

	private void RefreshControllerFocusGraph()
	{
		var rows = GetControllerUiRows();
		foreach (var row in rows)
			ResetFocusNeighbors(row);

		foreach (var row in rows)
			ConfigureHorizontalNeighbors(row);

		for (int index = 0; index < rows.Count - 1; index++)
			ConfigureVerticalNeighbors(rows[index], rows[index + 1]);
	}

	private void ResetFocusNeighbors(IReadOnlyList<Button> row)
	{
		foreach (var button in row)
		{
			var selfPath = button.GetPathTo(button);
			button.FocusNeighborLeft = selfPath;
			button.FocusNeighborRight = selfPath;
			button.FocusNeighborTop = selfPath;
			button.FocusNeighborBottom = selfPath;
		}
	}

	private void ConfigureHorizontalNeighbors(IReadOnlyList<Button> row)
	{
		if (row.Count == 0)
			return;

		for (int index = 0; index < row.Count; index++)
		{
			var current = row[index];
			var left = row[(index - 1 + row.Count) % row.Count];
			var right = row[(index + 1) % row.Count];
			current.FocusNeighborLeft = current.GetPathTo(left);
			current.FocusNeighborRight = current.GetPathTo(right);
		}
	}

	private void ConfigureVerticalNeighbors(IReadOnlyList<Button> upperRow, IReadOnlyList<Button> lowerRow)
	{
		if (upperRow.Count == 0 || lowerRow.Count == 0)
			return;

		foreach (var upper in upperRow)
			upper.FocusNeighborBottom = upper.GetPathTo(FindNearestButtonByX(lowerRow, GetControlCenterX(upper)));

		foreach (var lower in lowerRow)
			lower.FocusNeighborTop = lower.GetPathTo(FindNearestButtonByX(upperRow, GetControlCenterX(lower)));
	}

	private static Button FindNearestButtonByX(IReadOnlyList<Button> row, float sourceCenterX)
	{
		var nearest = row[0];
		var nearestDistance = Mathf.Abs(GetControlCenterX(nearest) - sourceCenterX);

		for (int index = 1; index < row.Count; index++)
		{
			var candidate = row[index];
			var distance = Mathf.Abs(GetControlCenterX(candidate) - sourceCenterX);
			if (distance >= nearestDistance)
				continue;

			nearest = candidate;
			nearestDistance = distance;
		}

		return nearest;
	}

	private static float GetControlCenterX(Control control)
	{
		var rect = control.GetGlobalRect();
		return rect.Position.X + (rect.Size.X * 0.5f);
	}

	private bool ShouldHandleControllerInput(int device)
	{
		return ControllerService.Instance?.ShouldHandleMenuInput(device) ?? true;
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
		UiStyle.StyleTopBarButton(_dropdownButton);
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
		ResetUiNavigationState();
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
		ResetUiNavigationState();
		CallDeferred(nameof(RefreshControllerFocusGraph));
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
		ResetUiNavigationState();
		var tree = GetTree();
		tree.ChangeSceneToFile("res://FriendsList.tscn");
	}

	private void GoProfileSettings()
	{
		ResetUiNavigationState();
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
