using Godot;
using PGEmu.app;
using PGEmu.Services;
using PGEmu.Services.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FriendRelationshipStatus = PGEmu.Services.FriendStatus;


public partial class FoundUserProfile : Control
{
	private const string SettingsParentReturnSceneMeta = "pgemu_profile_settings_parent_return_scene";
	private const string ReturnSceneMetaKey = "pgemu_return_scene";
	private const string FoundUserProfileScene = "res://FoundUserProfile.tscn";
	private const string DefaultReturnScene = "res://profile.tscn";
	private const string FoundProfileRootReturnSceneMeta = "pgemu_found_profile_root_return_scene";
	private const string FoundProfileBackStackMeta = "pgemu_found_profile_back_stack";
	private const string CollectionsFocusMetaKey = "pgemu_collections_focus_name";
	private const string FoundProfileUsernameMeta = "pgemu_found_profile_username";
	private const string FoundProfileUserIdMeta = "pgemu_found_profile_user_id";
	private const string FriendsListOwnerMeta = "pgemu_friends_list_owner_username";
	private const string ShowcaseTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/TileGrid";
	private const string RecentGamesTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/TileGrid";
	private const string FriendsTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/TileGrid";
	private const string ShowcaseFooterPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/FooterRow";
	private const string AvatarCardPath = "Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer2";
	private const string AvatarFrameMarginPath = AvatarCardPath + "/AvatarFrameMargin";
	private const string AvatarFramePath = AvatarFrameMarginPath + "/AvatarFrame";
	private const string AvatarInnerMarginPath = AvatarFramePath + "/AvatarInnerMargin";
	private const string AvatarAspectPath = AvatarInnerMarginPath + "/AspectRatioContainer";
	private const string AvatarStackPath = AvatarAspectPath + "/AvatarStack";
	private const string HoverFeedbackAppliedMeta = "pgemu_profile_hover_feedback_applied";
	private const string SceneBackgroundMaterialMeta = "pgemu_profile_scene_background_material_local";
	private const int MaxTileCount = 4;
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

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
	private ProfileResponse profile = new();
	
	public bool blockedUsers;
	private bool userBlocked = false;
	
	
	private Button _back = null!;
	private Label _gamer_tag = null!;
	private Label _profile_note = null!;
	private Label _title_gamertag = null!;
	private Label? _profile_status = null;
	
	private Button? _addFriend = null;
	private Button _friends_list = null!;
	private Button? _showcaseSeeAll = null;
	private Button? _recentGamesSeeAll = null;
	private Button? _profileSettingsShortcut = null;
	private MenuButton? _dropdownButton = null;
	private PopupMenu? _dropdownOptions = null; 
	
	private TextureRect _avatar = null!;
	private int _uiHorizontalAxisDir;
	private long _uiHorizontalAxisNextMs;
	private int _uiVerticalAxisDir;
	private long _uiVerticalAxisNextMs;
	private int _uiRowIndex = -1;
	private int _uiColumnIndex = -1;
	private readonly Dictionary<Button, string> _friendTileUsernames = new();
	private ProfileVisualStyleCatalog.ResolvedStyle _visualStyle =
		ProfileVisualStyleCatalog.Resolve(null, null, null);
	private bool _openingFriendProfile;
	private bool _openingSectionScene;
	private FriendRelationshipStatus? _friendRelationshipStatus;

	private sealed class FoundProfileBackEntry
	{
		public string Username { get; set; } = string.Empty;
		public string UserId { get; set; } = string.Empty;
		public string Bio { get; set; } = string.Empty;
		public string AvatarUrl { get; set; } = string.Empty;
		public string ProfileAccent { get; set; } = string.Empty;
		public string AvatarFrame { get; set; } = string.Empty;
		public string ProfileBackground { get; set; } = string.Empty;
	}
	

	public override async void _Ready()
	{
		profile = ResolveInitialProfile();
		CaptureRootReturnScene();

		_back = GetNode<Button>(BackPath);
		if (!_back.IsConnected(Button.SignalName.Pressed, Callable.From(GoBack)))
			_back.Pressed += GoBack;

		_profileSettingsShortcut = GetNodeOrNull<Button>(ProfileSettingsShortcutPath);
		if (_profileSettingsShortcut != null &&
			!_profileSettingsShortcut.IsConnected(Button.SignalName.Pressed, Callable.From(GoProfileSettings)))
		{
			_profileSettingsShortcut.Pressed += GoProfileSettings;
		}
		if (_profileSettingsShortcut != null)
			_profileSettingsShortcut.Visible = false;
		
		_friends_list = GetNode<Button>(FriendsListPath);
		if (!_friends_list.IsConnected(Button.SignalName.Pressed, Callable.From(GoFriendsList)))
		{
			_friends_list.Pressed += GoFriendsList;
		}

		_showcaseSeeAll = GetNodeOrNull<Button>(ShowcaseFooterPath + "/Button");
		if (_showcaseSeeAll != null && !_showcaseSeeAll.IsConnected(Button.SignalName.Pressed, Callable.From(GoAchievements)))
			_showcaseSeeAll.Pressed += GoAchievements;

		_recentGamesSeeAll = GetNodeOrNull<Button>(RecentGamesPath);
		if (_recentGamesSeeAll != null && !_recentGamesSeeAll.IsConnected(Button.SignalName.Pressed, Callable.From(GoGameSelect)))
			_recentGamesSeeAll.Pressed += GoGameSelect;
		
		_addFriend = GetNodeOrNull<Button>(AddFriendPath);
		if (_addFriend != null && !_addFriend.IsConnected(Button.SignalName.Pressed, Callable.From(AddFriend)))
			_addFriend.Pressed += AddFriend;
		
		_dropdownButton = GetNodeOrNull<MenuButton>(DropdownOptionsPath);
		_dropdownOptions = _dropdownButton?.GetPopup();
		if (_dropdownOptions != null)
		{
			_dropdownOptions.Clear();
			_dropdownOptions.AddItem("Message", 0);
			if (!string.IsNullOrWhiteSpace(profile.UserId))
				userBlocked = await _friendService.GetIsBlocked(profile.UserId);
			_dropdownOptions.AddItem(userBlocked ? "Unblock" : "Block", 1);
			_dropdownOptions.IdPressed += OnDropdownSelected;
		}

		if (!string.IsNullOrWhiteSpace(ProfileStatusPath))
		{
			_profile_status = GetNodeOrNull<Label>(ProfileStatusPath);
		}
		
		_title_gamertag = GetNode<Label>(TitleGamertagPath);
		_gamer_tag = GetNode<Label>(GamerTagPath);
		_profile_note = GetNode<Label>(ProfileNotePath);
		_avatar = GetNode<TextureRect>(AvatarPath);
		ApplyAvatarMaskShader();

		// Keep navigation usable while profile data loads.
		_title_gamertag.Text = $"{profile.Username}'s Profile";
		_gamer_tag.Text = profile.Username;
		_profile_note.Text = $"\"{profile.Bio}\"";
		ApplyTileContent(RecentGamesTileGridPath, Array.Empty<string>(), "Loading games...");
		ApplyTileContent(FriendsTileGridPath, Array.Empty<string>(), "Loading friends...");
		
		ApplyThemeAesthetic();
		StartBackgroundTransition();
		BindFriendTileButtons();
		BindShowcaseTileButtons();
		BindRecentGameTileButtons();
		_ = LoadProfileAsync();
		CallDeferred(nameof(RefreshControllerFocusGraph));
	}

	private void ApplyAvatarMaskShader()
	{
		var avatarMaterial = _avatar.Material as ShaderMaterial;
		if (avatarMaterial == null)
		{
			avatarMaterial = new ShaderMaterial
			{
				Shader = GD.Load<Shader>(GetAvatarShaderPath())
			};
			_avatar.Material = avatarMaterial;
		}
		else if (avatarMaterial.Shader?.ResourcePath != GetAvatarShaderPath())
		{
			avatarMaterial.Shader = GD.Load<Shader>(GetAvatarShaderPath());
		}

		var style = _visualStyle;
		avatarMaterial.SetShaderParameter("width", style.AvatarFrame.Width);
		if (style.AvatarFrame.Id != "circle")
			avatarMaterial.SetShaderParameter("radius", style.AvatarFrame.Radius);
		avatarMaterial.SetShaderParameter("stroke_width", style.AvatarFrame.StrokeWidth);
		avatarMaterial.SetShaderParameter("stroke_color", style.Background.Accent);
		avatarMaterial.SetShaderParameter("edge_softness", 0.006f);
	}

	private string GetAvatarShaderPath()
	{
		return _visualStyle.AvatarFrame.Id == "circle"
			? "res://ShaderSlop/circle.gdshader"
			: "res://ShaderSlop/RoundedAvatarFrame.gdshader";
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
		var topRow = _profileSettingsShortcut != null && _profileSettingsShortcut.Visible
			? new Button?[] { _back, _profileSettingsShortcut }
			: new Button?[] { _back };

		var actionRow = _addFriend != null && _dropdownButton != null
			? new Button?[] { _addFriend, _dropdownButton }
			: _addFriend != null
				? new Button?[] { _addFriend }
				: _dropdownButton != null
					? new Button?[] { _dropdownButton }
					: Array.Empty<Button?>();

		var rows = ControllerService.BuildVisibleRows(topRow, actionRow);
		rows.AddRange(BuildSectionTileRows());
		rows.AddRange(ControllerService.BuildVisibleRows(new Button?[] { _showcaseSeeAll, _recentGamesSeeAll, _friends_list }));
		return rows;
	}

	private List<List<Button>> BuildSectionTileRows()
	{
		var showcaseTiles = GetTileButtons(ShowcaseTileGridPath);
		var recentTiles = GetTileButtons(RecentGamesTileGridPath);
		var friendTiles = GetTileButtons(FriendsTileGridPath);

		var maxRows = Mathf.Max(showcaseTiles.Count, Mathf.Max(recentTiles.Count, friendTiles.Count));
		if (maxRows <= 0)
			return new List<List<Button>>();

		var rows = new List<List<Button>>(maxRows);
		for (int index = 0; index < maxRows; index++)
		{
			var row = new List<Button>(3);
			TryAddSectionTile(row, showcaseTiles, index);
			TryAddSectionTile(row, recentTiles, index);
			TryAddSectionTile(row, friendTiles, index);

			if (row.Count > 0)
				rows.Add(row);
		}

		return rows;
	}

	private static void TryAddSectionTile(ICollection<Button> row, IReadOnlyList<Button> sectionTiles, int index)
	{
		if (index < 0 || index >= sectionTiles.Count)
			return;

		var button = sectionTiles[index];
		if (!GodotObject.IsInstanceValid(button) || !button.Visible || button.Disabled)
			return;

		ControllerService.PrepareFocusable(button);
		row.Add(button);
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

	private ProfileResponse ResolveInitialProfile()
	{
		var resolved = CloneProfile(Global.foundProfile) ?? new ProfileResponse();
		var tree = GetTree();

		if (tree.HasMeta(FoundProfileUsernameMeta))
		{
			var requestedUsername = tree.GetMeta(FoundProfileUsernameMeta).AsString();
			if (!string.IsNullOrWhiteSpace(requestedUsername))
				resolved.Username = requestedUsername.Trim();
		}

		if (tree.HasMeta(FoundProfileUserIdMeta))
		{
			var requestedUserId = tree.GetMeta(FoundProfileUserIdMeta).AsString();
			if (!string.IsNullOrWhiteSpace(requestedUserId))
				resolved.UserId = requestedUserId.Trim();
		}

		resolved.Username = string.IsNullOrWhiteSpace(resolved.Username) ? "Player" : resolved.Username.Trim();
		resolved.Bio = string.IsNullOrWhiteSpace(resolved.Bio) ? "No bio yet." : resolved.Bio;
		resolved.AvatarUrl ??= string.Empty;
		resolved.UserId ??= string.Empty;
		return resolved;
	}

	private void CaptureRootReturnScene()
	{
		var tree = GetTree();
		var incomingReturnScene = tree.HasMeta(ReturnSceneMetaKey)
			? tree.GetMeta(ReturnSceneMetaKey).AsString()
			: null;

		if (IsFoundUserProfileScene(incomingReturnScene))
			return;

		var rootReturnScene = string.IsNullOrWhiteSpace(incomingReturnScene)
			? DefaultReturnScene
			: incomingReturnScene.Trim();

		tree.SetMeta(FoundProfileRootReturnSceneMeta, rootReturnScene);
		if (tree.HasMeta(FoundProfileBackStackMeta))
			tree.RemoveMeta(FoundProfileBackStackMeta);
	}

	private static bool IsFoundUserProfileScene(string? scene)
	{
		return string.Equals(scene?.Trim(), FoundUserProfileScene, StringComparison.OrdinalIgnoreCase);
	}

	private static List<FoundProfileBackEntry> ReadFoundProfileBackStack(SceneTree tree)
	{
		if (!tree.HasMeta(FoundProfileBackStackMeta))
			return new List<FoundProfileBackEntry>();

		var rawStack = tree.GetMeta(FoundProfileBackStackMeta).AsString();
		if (string.IsNullOrWhiteSpace(rawStack))
			return new List<FoundProfileBackEntry>();

		try
		{
			return JsonSerializer.Deserialize<List<FoundProfileBackEntry>>(rawStack, JsonOptions)
				?? new List<FoundProfileBackEntry>();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"FoundUserProfile: could not read profile back stack: {exception.Message}");
			return new List<FoundProfileBackEntry>();
		}
	}

	private static void SaveFoundProfileBackStack(SceneTree tree, List<FoundProfileBackEntry> stack)
	{
		if (stack.Count == 0)
		{
			if (tree.HasMeta(FoundProfileBackStackMeta))
				tree.RemoveMeta(FoundProfileBackStackMeta);
			return;
		}

		tree.SetMeta(FoundProfileBackStackMeta, JsonSerializer.Serialize(stack, JsonOptions));
	}

	private void PushCurrentFoundProfileForBack(SceneTree tree)
	{
		var stack = ReadFoundProfileBackStack(tree);
		stack.Add(new FoundProfileBackEntry
		{
			Username = profile.Username ?? string.Empty,
			UserId = profile.UserId ?? string.Empty,
			Bio = profile.Bio ?? string.Empty,
			AvatarUrl = profile.AvatarUrl ?? string.Empty,
			ProfileAccent = profile.ProfileAccent ?? string.Empty,
			AvatarFrame = profile.AvatarFrame ?? string.Empty,
			ProfileBackground = profile.ProfileBackground ?? string.Empty
		});

		SaveFoundProfileBackStack(tree, stack);
		tree.SetMeta(ReturnSceneMetaKey, FoundUserProfileScene);
	}

	private static void RestoreFoundProfile(SceneTree tree, FoundProfileBackEntry entry)
	{
		var restored = new ProfileResponse
		{
			Username = string.IsNullOrWhiteSpace(entry.Username) ? "Player" : entry.Username.Trim(),
			UserId = entry.UserId ?? string.Empty,
			Bio = string.IsNullOrWhiteSpace(entry.Bio) ? "No bio yet." : entry.Bio,
			AvatarUrl = entry.AvatarUrl ?? string.Empty,
			ProfileAccent = entry.ProfileAccent ?? string.Empty,
			AvatarFrame = entry.AvatarFrame ?? string.Empty,
			ProfileBackground = entry.ProfileBackground ?? string.Empty
		};

		Global.foundProfile = restored;
		tree.SetMeta(FoundProfileUsernameMeta, restored.Username);
		tree.SetMeta(FoundProfileUserIdMeta, restored.UserId);
	}

	private static ProfileResponse? CloneProfile(ProfileResponse? source)
	{
		if (source == null)
			return null;

		return new ProfileResponse
		{
			Message = source.Message,
			UserId = source.UserId,
			Username = source.Username,
			Bio = source.Bio,
			AvatarUrl = source.AvatarUrl,
			ProfileAccent = source.ProfileAccent,
			AvatarFrame = source.AvatarFrame,
			ProfileBackground = source.ProfileBackground
		};
	}

	private async Task LoadProfileAsync()
	{
		try
		{
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
			{
				return;
			}

			var tree = GetTree();
			var requestedUsername = tree.HasMeta(FoundProfileUsernameMeta)
				? tree.GetMeta(FoundProfileUsernameMeta).AsString()?.Trim()
				: profile.Username?.Trim();

			ProfileResponse? apiProfile = null;

			if (!string.IsNullOrWhiteSpace(requestedUsername))
			{
				apiProfile = await _profileService.GetUserProfile(requestedUsername);
			}

			if (apiProfile != null)
			{
				profile = apiProfile;
				Global.foundProfile = apiProfile;
				tree.SetMeta(FoundProfileUsernameMeta, profile.Username ?? string.Empty);
				tree.SetMeta(FoundProfileUserIdMeta, profile.UserId ?? string.Empty);
			}
			else
			{
				GD.PrintErr($"FoundUserProfile: API profile lookup failed for '{requestedUsername ?? "<empty>"}'; using cached profile.");
			}

			await LoadSectionDataAsync(profile.Username);
			ApplyProfileVisualStyle(profile);

			if (GodotObject.IsInstanceValid(_title_gamertag))
			{
				var titleName = string.IsNullOrWhiteSpace(profile.Username) ? "Player" : profile.Username;
				_title_gamertag.Text = $"{titleName}'s Profile";
			}

			if (GodotObject.IsInstanceValid(_gamer_tag))
			{
				_gamer_tag.Text = string.IsNullOrWhiteSpace(profile.Username) ? "Player" : profile.Username;
			}

			if (GodotObject.IsInstanceValid(_profile_note))
			{
				_profile_note.Text = string.IsNullOrWhiteSpace(profile.Bio) ? "\"No bio yet.\"" : $"\"{profile.Bio}\"";
			}

			if (_dropdownOptions != null && !string.IsNullOrWhiteSpace(profile.UserId))
			{
				userBlocked = await _friendService.GetIsBlocked(profile.UserId);
				var blockIndex = _dropdownOptions.GetItemIndex(1);
				if (blockIndex >= 0)
				{
					_dropdownOptions.SetItemText(blockIndex, userBlocked ? "Unblock" : "Block");
				}
			}

			await RefreshFriendActionButtonAsync();

			if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
			{
				GD.Print($"Loaded profile from API for username '{profile.Username}' and userId '{profile.UserId}'");
				GD.Print("Avatar: ", profile.AvatarUrl);
				_ = LoadAvatar(profile.AvatarUrl);
			}
			else
			{
				GD.Print($"Loaded profile from API for username '{profile.Username}' and userId '{profile.UserId}', but no avatar URL was returned.");
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

	private void ApplyProfileVisualStyle(ProfileResponse profile)
	{
		_visualStyle = ProfileVisualStyleCatalog.Resolve(
			profile.ProfileAccent,
			profile.AvatarFrame,
			profile.ProfileBackground);
		ApplyAvatarMaskShader();
		ApplyThemeAesthetic();
		StartBackgroundTransition(0.85f);
	}

	private void ApplyThemeAesthetic()
	{
		ApplySceneBackgroundShader();

		var theme = _visualStyle.Background;
		var accentPrimary = theme.Accent;
		var cardSurface = _visualStyle.Background.CardSurface;
		var cardSurfaceAlt = _visualStyle.Background.CardSurfaceAlt;
		var cardSurfaceInset = _visualStyle.Background.CardSurfaceInset;
		var cardBorder = WithAlpha(Mix(accentPrimary, new Color(0.56f, 0.48f, 0.76f, 1f), 0.50f), 0.46f);
		var cardBorderStrong = WithAlpha(Mix(accentPrimary, new Color(0.97f, 0.95f, 1f, 1f), 0.22f), 0.62f);
		var avatarBorder = WithAlpha(accentPrimary, 0.62f);
		var chipSurface = _visualStyle.Background.ChipSurface;
		var chipAccent = WithAlpha(accentPrimary, 0.84f);
		var showcaseAccent = WithAlpha(theme.ShowcaseAccent, 0.92f);
		var recentAccent = WithAlpha(theme.RecentAccent, 0.92f);
		var friendsAccent = WithAlpha(theme.FriendsAccent, 0.92f);
		var separatorColor = _visualStyle.Background.Separator;
		var themeTileAccents = theme.TileAccents;

		UiStyle.StyleTopBarButton(_back);
		ApplyProfileHoverFeedback(_back, scaleUp: 1.08f);
		UiStyle.TightenButtonContentPadding(_back, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_back, chipSurface, showcaseAccent, isChip: true, borderWidth: 2);

		if (_profileSettingsShortcut != null)
		{
			UiStyle.StyleTopBarButton(_profileSettingsShortcut);
			ApplyProfileHoverFeedback(_profileSettingsShortcut, scaleUp: 1.08f);
			UiStyle.TightenButtonContentPadding(_profileSettingsShortcut, horizontal: 8f, vertical: 3f);
			ApplyButtonTheme(_profileSettingsShortcut, chipSurface, showcaseAccent, isChip: true);
		}

		if (_addFriend != null)
		{
			UiStyle.StyleTopBarButton(_addFriend);
			ApplyProfileHoverFeedback(_addFriend, scaleUp: 1.08f);
			UiStyle.TightenButtonContentPadding(_addFriend, horizontal: 8f, vertical: 3f);
			ApplyButtonTheme(_addFriend, chipSurface, chipAccent, isChip: true);
		}

		if (_dropdownButton != null)
		{
			UiStyle.StyleTopBarButton(_dropdownButton);
			ApplyProfileHoverFeedback(_dropdownButton, scaleUp: 1.08f);
			UiStyle.TightenButtonContentPadding(_dropdownButton, horizontal: 8f, vertical: 3f);
			ApplyButtonTheme(_dropdownButton, chipSurface, showcaseAccent, isChip: true);
		}

		if (_dropdownOptions != null)
			UiStyle.StylePopupMenu(_dropdownOptions);

		UiStyle.StyleTitleLabel(_title_gamertag);
		_title_gamertag.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 1f, 0.98f));
		UiStyle.StyleTitleLabel(_gamer_tag);
		UiStyle.StyleStatusLabel(_profile_note);
		if (_profile_status != null)
			UiStyle.StyleMetaLabel(_profile_status);

		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/Showcase", showcaseAccent);
		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/Label", recentAccent);
		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Label", friendsAccent);

		ApplyCardStyle(AvatarCardPath, cardSurfaceAlt, avatarBorder, _visualStyle.AvatarFrame.PanelRadius, 1);
		ApplyAvatarFrameLayout();
		ApplyCardStyle("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer", cardSurface, cardBorderStrong, 16, 1);
		ApplyCardStyle("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer", cardSurfaceInset, cardBorder, 14, 1);

		ApplyCardStyle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection", cardSurfaceAlt, WithAlpha(showcaseAccent, 0.40f), 16, 1);
		ApplyCardStyle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2", cardSurface, WithAlpha(recentAccent, 0.38f), 16, 1);
		ApplyCardStyle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends", cardSurfaceAlt, WithAlpha(friendsAccent, 0.40f), 16, 1);

		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/HSeparator", WithAlpha(showcaseAccent, 0.32f));
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/HSeparator", WithAlpha(recentAccent, 0.30f));
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/HSeparator", WithAlpha(friendsAccent, 0.30f));
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/GamesAndFriendsSeparator2", separatorColor);
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/GamesAndFriendsSeparator", separatorColor);

		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/TileGrid",
			showcaseAccent,
			themeTileAccents);
		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/TileGrid",
			recentAccent,
			themeTileAccents);
		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/TileGrid",
			friendsAccent,
			themeTileAccents);

		StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/FooterRow/Button", chipSurface, showcaseAccent);
		StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/FooterRow/Button", chipSurface, recentAccent);
		StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/FooterRow/Button", chipSurface, friendsAccent);
	}

	private void ApplySceneBackgroundShader()
	{
		if (GetNodeOrNull<ColorRect>("Bg") is not ColorRect bg)
			return;

		bg.Color = _visualStyle.Background.Overlay;
		if (bg.Material is not ShaderMaterial material)
			return;

		if (!bg.HasMeta(SceneBackgroundMaterialMeta))
		{
			material = material.Duplicate() as ShaderMaterial ?? material;
			bg.Material = material;
			bg.SetMeta(SceneBackgroundMaterialMeta, true);
		}

		material.SetShaderParameter("tint", _visualStyle.Background.SceneTint);
		material.SetShaderParameter("highlight_color", _visualStyle.Background.SceneHighlight);
		material.SetShaderParameter("highlight_thickness", 0.0f);
	}

	private void ApplyAvatarFrameLayout()
	{
		var frame = _visualStyle.AvatarFrame;
		var avatarSize = new Vector2(frame.AvatarSize, frame.AvatarSize);

		if (GetNodeOrNull<MarginContainer>(AvatarFrameMarginPath) is MarginContainer frameMargin)
			SetMarginConstants(frameMargin, frame.FrameMargin);

		if (GetNodeOrNull<MarginContainer>(AvatarInnerMarginPath) is MarginContainer innerMargin)
			SetMarginConstants(innerMargin, frame.InnerMargin);

		if (GetNodeOrNull<PanelContainer>(AvatarFramePath) is PanelContainer framePanel)
		{
			framePanel.ClipContents = true;
			framePanel.AddThemeStyleboxOverride(
				"panel",
				CreatePanelStyle(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), frame.PanelRadius, 0));
		}

		if (GetNodeOrNull<PanelContainer>(AvatarCardPath) is PanelContainer avatarCard)
		{
			var existing = avatarCard.GetThemeStylebox("panel") as StyleBoxFlat;
			var background = existing?.BgColor ?? _visualStyle.Background.CardSurfaceAlt;
			var border = existing?.BorderColor ?? WithAlpha(_visualStyle.Background.Accent, 0.62f);
			var borderWidth = existing?.BorderWidthLeft ?? 1;
			avatarCard.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, frame.PanelRadius, borderWidth));
		}

		if (GetNodeOrNull<Control>(AvatarAspectPath) is Control aspect)
		{
			aspect.ClipContents = true;
			aspect.CustomMinimumSize = avatarSize;
		}

		if (GetNodeOrNull<Control>(AvatarStackPath) is Control stack)
		{
			stack.ClipContents = true;
			stack.CustomMinimumSize = avatarSize;
		}

		_avatar.ClipContents = true;
		_avatar.CustomMinimumSize = avatarSize;
		_avatar.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_avatar.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_avatar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_avatar.OffsetLeft = 0f;
		_avatar.OffsetTop = 0f;
		_avatar.OffsetRight = 0f;
		_avatar.OffsetBottom = 0f;
	}

	private static Color[] BuildCohesiveTileAccents(Color accent)
	{
		return new[]
		{
			WithAlpha(accent, 1f),
			WithAlpha(ShiftHue(accent, -0.11f, saturationBoost: 0.05f, valueBoost: 0.03f), 1f),
			WithAlpha(ShiftHue(accent, 0.14f, saturationBoost: 0.04f, valueBoost: 0.03f), 1f),
			WithAlpha(Mix(ShiftHue(accent, 0.24f, saturationBoost: 0.02f, valueBoost: 0.02f), accent, 0.25f), 1f)
		};
	}

	private async Task LoadSectionDataAsync(string? username)
	{
		if (string.IsNullOrWhiteSpace(username))
		{
			ApplyTileContent(RecentGamesTileGridPath, Array.Empty<string>(), "No games found");
			ApplyTileContent(FriendsTileGridPath, Array.Empty<string>(), "No friends yet");
			return;
		}

		try
		{
			var auth = AuthService.Instance;
			if (auth == null)
			{
				ApplyTileContent(RecentGamesTileGridPath, Array.Empty<string>(), "No games found");
				ApplyTileContent(FriendsTileGridPath, Array.Empty<string>(), "No friends yet");
				return;
			}

			var encodedUsername = Uri.EscapeDataString(username.Trim());
			var gamesTask = auth.SendAuthorizedRequest($"http://localhost:5276/api/profile/{encodedUsername}/games?limit={MaxTileCount}");
			var friendsTask = auth.SendAuthorizedRequest($"http://localhost:5276/api/profile/{encodedUsername}/friends?limit={MaxTileCount}");

			await Task.WhenAll(gamesTask, friendsTask);
			var gameItems = DeserializeList<ProfileGameSummary>(await gamesTask);
			var friendItems = DeserializeList<UserSearchResultResponse>(await friendsTask);

			var gameLabels = new List<string>(MaxTileCount);
			foreach (var game in gameItems)
			{
				var label = FormatRecentGameLabel(game);
				if (string.IsNullOrWhiteSpace(label))
					continue;

				gameLabels.Add(label);
				if (gameLabels.Count >= MaxTileCount)
					break;
			}

			var visibleFriends = new List<UserSearchResultResponse>(MaxTileCount);
			foreach (var friend in friendItems)
			{
				var label = friend.Username?.Trim();
				if (string.IsNullOrWhiteSpace(label))
					continue;

				friend.Username = label;
				visibleFriends.Add(friend);
				if (visibleFriends.Count >= MaxTileCount)
					break;
			}

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			ApplyTileContent(RecentGamesTileGridPath, gameLabels, "No games found");
			ApplyFriendTileContent(visibleFriends, "No friends yet");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"FoundUserProfile section load failed: {exception.Message}");
			ApplyTileContent(RecentGamesTileGridPath, Array.Empty<string>(), "No games found");
			ApplyFriendTileContent(Array.Empty<UserSearchResultResponse>(), "No friends yet");
		}
	}

	private static IReadOnlyList<T> DeserializeList<T>(JsonElement? payload)
	{
		if (!payload.HasValue || payload.Value.ValueKind != JsonValueKind.Array)
			return Array.Empty<T>();

		try
		{
			var data = JsonSerializer.Deserialize<List<T>>(payload.Value.GetRawText(), JsonOptions);
			return data ?? new List<T>();
		}
		catch
		{
			return Array.Empty<T>();
		}
	}

	private static string FormatRecentGameLabel(ProfileGameSummary game)
	{
		var gameName = string.IsNullOrWhiteSpace(game.ExternalGameId)
			? "Unknown Game"
			: game.ExternalGameId.Trim();

		if (game.PlaytimeMinutes <= 0)
			return gameName;

		return $"{gameName}  |  {FormatPlaytime(game.PlaytimeMinutes)}";
	}

	private static string FormatPlaytime(int totalMinutes)
	{
		if (totalMinutes < 60)
			return $"{totalMinutes}m";

		var hours = totalMinutes / 60;
		var minutes = totalMinutes % 60;
		return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
	}

	private void ApplyTileContent(string containerPath, IReadOnlyList<string> items, string emptyText)
	{
		var buttons = GetTileButtons(containerPath);
		if (buttons.Count == 0)
			return;

		if (items.Count == 0)
		{
			for (int index = 0; index < buttons.Count; index++)
			{
				var button = buttons[index];
				button.Visible = index == 0;
				button.Disabled = index == 0;
				button.Text = index == 0 ? emptyText : string.Empty;
				button.TooltipText = index == 0 ? emptyText : string.Empty;
			}

			return;
		}

		for (int index = 0; index < buttons.Count; index++)
		{
			var button = buttons[index];
			if (index < items.Count)
			{
				var label = items[index];
				button.Visible = true;
				button.Disabled = false;
				button.Text = label;
				button.TooltipText = label;
			}
			else
			{
				button.Visible = false;
				button.Disabled = false;
				button.Text = string.Empty;
				button.TooltipText = string.Empty;
			}
		}
	}

	private void ApplyFriendTileContent(IReadOnlyList<UserSearchResultResponse> friends, string emptyText)
	{
		var buttons = GetTileButtons(FriendsTileGridPath);
		if (buttons.Count == 0)
			return;

		_friendTileUsernames.Clear();

		if (friends.Count == 0)
		{
			for (int index = 0; index < buttons.Count; index++)
			{
				var button = buttons[index];
				button.Visible = index == 0;
				button.Disabled = index == 0;
				button.Text = index == 0 ? emptyText : string.Empty;
				button.TooltipText = index == 0 ? emptyText : string.Empty;
			}

			return;
		}

		for (int index = 0; index < buttons.Count; index++)
		{
			var button = buttons[index];
			if (index < friends.Count)
			{
				var username = friends[index].Username?.Trim() ?? string.Empty;
				if (string.IsNullOrWhiteSpace(username))
				{
					button.Visible = false;
					button.Disabled = true;
					button.Text = string.Empty;
					button.TooltipText = string.Empty;
					continue;
				}

				button.Visible = true;
				button.Disabled = false;
				button.Text = username;
				button.TooltipText = $"View {username}'s profile";
				_friendTileUsernames[button] = username;
			}
			else
			{
				button.Visible = false;
				button.Disabled = true;
				button.Text = string.Empty;
				button.TooltipText = string.Empty;
			}
		}
	}

	private List<Button> GetTileButtons(string containerPath)
	{
		var container = GetNodeOrNull<Node>(containerPath);
		if (container == null)
			return new List<Button>();

		var buttons = new List<Button>();
		foreach (var child in container.GetChildren())
		{
			if (child is Button button)
				buttons.Add(button);
		}

		return buttons;
	}

	private void BindFriendTileButtons()
	{
		var buttons = GetTileButtons(FriendsTileGridPath);
		foreach (var button in buttons)
		{
			var capturedButton = button;
			capturedButton.Pressed += async () => await OnFriendTilePressedAsync(capturedButton);
		}
	}

	private void BindShowcaseTileButtons()
	{
		var buttons = GetTileButtons(ShowcaseTileGridPath);
		foreach (var button in buttons)
		{
			var capturedButton = button;
			capturedButton.Pressed += () => OpenCollectionsFromShowcaseTile(capturedButton);
		}
	}

	private void BindRecentGameTileButtons()
	{
		var buttons = GetTileButtons(RecentGamesTileGridPath);
		foreach (var button in buttons)
		{
			var capturedButton = button;
			capturedButton.Pressed += () => OpenGameSelectFromRecentTile(capturedButton);
		}
	}

	private void OpenCollectionsFromShowcaseTile(Button tileButton)
	{
		if (!IsActionableTile(tileButton) || _openingSectionScene)
			return;

		_openingSectionScene = true;
		try
		{
			AudioManager.Instance?.PlaySelect();
			var tree = GetTree();
			tree.SetMeta(ReturnSceneMetaKey, "res://FoundUserProfile.tscn");
			var selectedCollectionName = tileButton.Text?.Trim();
			if (!string.IsNullOrWhiteSpace(selectedCollectionName))
				tree.SetMeta(CollectionsFocusMetaKey, selectedCollectionName);
			else if (tree.HasMeta(CollectionsFocusMetaKey))
				tree.RemoveMeta(CollectionsFocusMetaKey);

			tree.ChangeSceneToFile("res://Collections.tscn");
		}
		finally
		{
			_openingSectionScene = false;
		}
	}

	private void OpenGameSelectFromRecentTile(Button tileButton)
	{
		if (!IsActionableTile(tileButton) || _openingSectionScene)
			return;

		_openingSectionScene = true;
		try
		{
			AudioManager.Instance?.PlaySelect();
			var tree = GetTree();
			tree.SetMeta(ReturnSceneMetaKey, "res://FoundUserProfile.tscn");
			tree.ChangeSceneToFile("res://GameSelect.tscn");
		}
		finally
		{
			_openingSectionScene = false;
		}
	}

	private static bool IsActionableTile(Button button)
	{
		if (!GodotObject.IsInstanceValid(button))
			return false;
		if (!button.Visible || button.Disabled)
			return false;

		return !string.IsNullOrWhiteSpace(button.Text);
	}

	private async Task OnFriendTilePressedAsync(Button button)
	{
		if (!_friendTileUsernames.TryGetValue(button, out var username) || string.IsNullOrWhiteSpace(username))
			username = button.Text;

		username = username?.Trim();
		if (string.IsNullOrWhiteSpace(username))
			return;

		await OpenFriendProfileByUsernameAsync(username);
	}

	private async Task OpenFriendProfileByUsernameAsync(string username)
	{
		if (_openingFriendProfile || string.IsNullOrWhiteSpace(username))
			return;

		_openingFriendProfile = true;
		try
		{
			AudioManager.Instance?.PlaySelect();
			var friendProfile = await _profileService.GetUserProfile(username.Trim());
			if (friendProfile == null)
				return;

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			Global.foundProfile = friendProfile;
			var tree = GetTree();
			PushCurrentFoundProfileForBack(tree);
			tree.SetMeta(FoundProfileUsernameMeta, friendProfile.Username ?? string.Empty);
			tree.SetMeta(FoundProfileUserIdMeta, friendProfile.UserId ?? string.Empty);
			tree.ChangeSceneToFile(FoundUserProfileScene);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Could not open friend profile '{username}': {exception.Message}");
		}
		finally
		{
			_openingFriendProfile = false;
		}
	}

	private void StyleTileGrid(string containerPath, Color sectionAccent, Color[] tileAccents)
	{
		var container = GetNodeOrNull<Node>(containerPath);
		if (container == null)
			return;

		int index = 0;
		foreach (Node child in container.GetChildren())
		{
			if (child is not Button button)
				continue;

			var accent = tileAccents[index % tileAccents.Length];
			ApplyTileTheme(button, sectionAccent, accent);
			index++;
		}
	}

	private void StyleFooterButton(string buttonPath, Color background, Color accent)
	{
		var button = GetNodeOrNull<Button>(buttonPath);
		if (button == null)
			return;

		button.Text = "See All";
		button.Alignment = HorizontalAlignment.Center;
		ApplyButtonTheme(button, background, accent, isChip: true);
		ApplyProfileHoverFeedback(button, scaleUp: 1.06f, shadowOffsetY: 3f);
	}

	private void ApplyTileTheme(Button button, Color sectionAccent, Color tileAccent)
	{
		var baseSurface = Mix(new Color(0.13f, 0.11f, 0.20f, 0.96f), sectionAccent, 0.10f);
		var background = Mix(baseSurface, tileAccent, 0.22f);
		var hover = Mix(background, tileAccent, 0.12f);
		var pressed = Mix(background, tileAccent, 0.06f);
		var border = WithAlpha(tileAccent, 0.82f);
		var focusBorder = Mix(tileAccent, new Color(0.97f, 0.95f, 1f, 1f), 0.18f);

		button.Flat = false;
		button.Alignment = HorizontalAlignment.Left;
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.AddThemeFontSizeOverride("font_size", 14);
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, border, 2, 16, 10f, 9f));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(hover, tileAccent, 2, 16, 10f, 9f));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(pressed, tileAccent, 2, 16, 10f, 9f));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(hover, focusBorder, 3, 16, 10f, 9f));
		button.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_focus_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		ApplyProfileHoverFeedback(button, scaleUp: 1.03f, duration: 0.10f, shadowOffsetY: 2f);
	}

	private static void ApplyProfileHoverFeedback(Button? button, float scaleUp = 1.04f, float duration = 0.11f, float shadowOffsetY = 3f)
	{
		if (button == null || button.HasMeta(HoverFeedbackAppliedMeta))
			return;

		UiStyle.AddHoverFeedback(button, scaleUp, duration);
		UiStyle.ApplyParallaxShadow(button, offsetY: shadowOffsetY);
		button.SetMeta(HoverFeedbackAppliedMeta, true);
	}

	private void StyleSectionTitle(string nodePath, Color color)
	{
		var label = GetNodeOrNull<Label>(nodePath);
		if (label == null)
			return;

		UiStyle.StyleTitleLabel(label);
		label.AddThemeColorOverride("font_color", new Color(color.R, color.G, color.B, 0.98f));
	}

	private void StyleSeparator(string nodePath, Color color)
	{
		if (GetNodeOrNull<CanvasItem>(nodePath) is CanvasItem separator)
			separator.Modulate = color;
	}

	private static void SetMarginConstants(MarginContainer margin, int value)
	{
		margin.AddThemeConstantOverride("margin_left", value);
		margin.AddThemeConstantOverride("margin_top", value);
		margin.AddThemeConstantOverride("margin_right", value);
		margin.AddThemeConstantOverride("margin_bottom", value);
	}

	private void ApplyCardStyle(string nodePath, Color background, Color border, int radius, int borderWidth)
	{
		var control = GetNodeOrNull<Control>(nodePath);
		if (control == null)
			return;

		control.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, radius, borderWidth));
	}

	private void ApplyButtonTheme(Button button, Color background, Color accent, bool isChip = false, int borderWidth = 1)
	{
		int radius = isChip ? 999 : 14;
		float horizontalPadding = isChip ? 10f : 9f;
		float verticalPadding = isChip ? 4f : 5f;
		var hover = Mix(background, accent, 0.11f);
		var pressed = Mix(background, accent, 0.05f);
		var focusBorder = Mix(accent, new Color(0.76f, 0.90f, 1f, 1f), 0.25f);
		var focusBorderWidth = Math.Max(borderWidth + 1, 2);

		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, WithAlpha(accent, isChip ? 0.70f : 0.56f), borderWidth, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(hover, accent, borderWidth, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(pressed, accent, borderWidth, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(hover, focusBorder, focusBorderWidth, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.20f, 0.20f, 0.23f, 0.55f), new Color(0.52f, 0.52f, 0.56f, 0.5f), 1, radius, horizontalPadding, verticalPadding));
		button.AddThemeColorOverride("font_color", new Color(0.95f, 0.94f, 1f, 0.98f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.95f, 0.94f, 1f, 0.98f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.95f, 0.94f, 1f, 0.98f));
		button.AddThemeColorOverride("font_focus_color", new Color(0.95f, 0.94f, 1f, 0.98f));
	}

	private void StartBackgroundTransition(float duration = 1.5f)
	{
		var bg = GetNodeOrNull<GlobalBackground>("/root/GlobalBackground");
		if (bg == null)
		{
			GD.PrintErr("GlobalBackground node not found!");
			return;
		}

		try
		{
			bg.StartProfileBackgroundTransition(_visualStyle.Background.Id, duration);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Gradient transition failed: {ex.Message}");
		}
	}

	private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int radius, int borderWidth)
	{
		return new StyleBoxFlat
		{
			BgColor = background,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			BorderColor = border,
			BorderBlend = true,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomRight = radius,
			CornerRadiusBottomLeft = radius
		};
	}

	private static StyleBoxFlat CreateButtonStyle(Color background, Color border, int borderWidth, int radius, float horizontalPadding, float verticalPadding)
	{
		return new StyleBoxFlat
		{
			BgColor = background,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			BorderColor = border,
			BorderBlend = true,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomRight = radius,
			CornerRadiusBottomLeft = radius,
			ContentMarginLeft = horizontalPadding,
			ContentMarginTop = verticalPadding,
			ContentMarginRight = horizontalPadding,
			ContentMarginBottom = verticalPadding
		};
	}

	private static Color WithAlpha(Color color, float alpha)
	{
		return new Color(color.R, color.G, color.B, alpha);
	}

	private static Color ShiftHue(Color color, float hueOffset, float saturationBoost = 0f, float valueBoost = 0f)
	{
		RgbToHsv(color, out var hue, out var saturation, out var value);
		return HsvToRgb(
			Wrap01(hue + hueOffset),
			Clamp01(saturation + saturationBoost),
			Clamp01(value + valueBoost),
			color.A);
	}

	private static void RgbToHsv(Color color, out float hue, out float saturation, out float value)
	{
		var max = MathF.Max(color.R, MathF.Max(color.G, color.B));
		var min = MathF.Min(color.R, MathF.Min(color.G, color.B));
		var delta = max - min;

		value = max;
		saturation = max <= 0f ? 0f : delta / max;

		if (delta <= 0.00001f)
		{
			hue = 0f;
			return;
		}

		if (MathF.Abs(max - color.R) <= 0.00001f)
			hue = ((color.G - color.B) / delta) % 6f;
		else if (MathF.Abs(max - color.G) <= 0.00001f)
			hue = ((color.B - color.R) / delta) + 2f;
		else
			hue = ((color.R - color.G) / delta) + 4f;

		hue = Wrap01(hue / 6f);
	}

	private static Color HsvToRgb(float hue, float saturation, float value, float alpha)
	{
		var h = Wrap01(hue) * 6f;
		var c = value * saturation;
		var x = c * (1f - MathF.Abs((h % 2f) - 1f));
		var m = value - c;

		float r;
		float g;
		float b;
		if (h < 1f)
			(r, g, b) = (c, x, 0f);
		else if (h < 2f)
			(r, g, b) = (x, c, 0f);
		else if (h < 3f)
			(r, g, b) = (0f, c, x);
		else if (h < 4f)
			(r, g, b) = (0f, x, c);
		else if (h < 5f)
			(r, g, b) = (x, 0f, c);
		else
			(r, g, b) = (c, 0f, x);

		return new Color(r + m, g + m, b + m, alpha);
	}

	private static float Clamp01(float value)
	{
		return Math.Clamp(value, 0f, 1f);
	}

	private static float Wrap01(float value)
	{
		value %= 1f;
		return value < 0f ? value + 1f : value;
	}

	private static Color Mix(Color from, Color to, float amount)
	{
		return new Color(
			from.R + ((to.R - from.R) * amount),
			from.G + ((to.G - from.G) * amount),
			from.B + ((to.B - from.B) * amount),
			from.A + ((to.A - from.A) * amount));
	}

	private void GoBack()
	{
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var stack = ReadFoundProfileBackStack(tree);
		if (stack.Count > 0)
		{
			var previousProfile = stack[^1];
			stack.RemoveAt(stack.Count - 1);
			SaveFoundProfileBackStack(tree, stack);
			RestoreFoundProfile(tree, previousProfile);
			tree.SetMeta(ReturnSceneMetaKey, FoundUserProfileScene);
			tree.ChangeSceneToFile(FoundUserProfileScene);
			return;
		}

		var returnScene = tree.HasMeta(FoundProfileRootReturnSceneMeta)
			? tree.GetMeta(FoundProfileRootReturnSceneMeta).AsString()
			: null;

		if (string.IsNullOrWhiteSpace(returnScene) || IsFoundUserProfileScene(returnScene))
		{
			returnScene = tree.HasMeta(ReturnSceneMetaKey)
				? tree.GetMeta(ReturnSceneMetaKey).AsString()
				: null;
		}

		if (string.IsNullOrWhiteSpace(returnScene) || IsFoundUserProfileScene(returnScene))
			returnScene = DefaultReturnScene;

		if (tree.HasMeta(FoundProfileRootReturnSceneMeta))
			tree.RemoveMeta(FoundProfileRootReturnSceneMeta);
		if (tree.HasMeta(FoundProfileBackStackMeta))
			tree.RemoveMeta(FoundProfileBackStackMeta);
		tree.SetMeta(ReturnSceneMetaKey, returnScene);
		tree.ChangeSceneToFile(returnScene);
	}

	private async Task RefreshFriendActionButtonAsync()
	{
		if (_addFriend == null)
			return;

		if (string.IsNullOrWhiteSpace(profile.UserId) ||
			string.Equals(profile.UserId, AuthService.Instance.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
		{
			_addFriend.Hide();
			ResetUiNavigationState();
			CallDeferred(nameof(RefreshControllerFocusGraph));
			return;
		}

		_addFriend.Show();
		_addFriend.Disabled = true;
		_addFriend.Text = "Checking...";

		var relationship = await _friendService.GetRelationship(profile.UserId);
		_friendRelationshipStatus = relationship.Status;

		if (!GodotObject.IsInstanceValid(_addFriend))
			return;

		switch (_friendRelationshipStatus)
		{
			case FriendRelationshipStatus.Accepted:
				_addFriend.Text = "Remove Friend";
				_addFriend.Disabled = false;
				break;
			case FriendRelationshipStatus.Pending:
				_addFriend.Text = "Pending";
				_addFriend.Disabled = true;
				break;
			case FriendRelationshipStatus.Blocked:
				_addFriend.Text = "Blocked";
				_addFriend.Disabled = true;
				break;
			default:
				_addFriend.Text = "Send Friend Request";
				_addFriend.Disabled = false;
				break;
		}

		ResetUiNavigationState();
		CallDeferred(nameof(RefreshControllerFocusGraph));
	}
	
	private async void AddFriend()
	{
		if (string.IsNullOrWhiteSpace(profile.UserId))
			return;

		if (_addFriend != null)
			_addFriend.Disabled = true;

		var success = _friendRelationshipStatus == FriendRelationshipStatus.Accepted
			? await _friendService.RemoveFriend(profile.UserId)
			: await _friendService.SendFriendRequest(profile.UserId);

		if (!success)
		{
			GD.PrintErr(_friendRelationshipStatus == FriendRelationshipStatus.Accepted
				? "Could not remove friend."
				: "Could not send friend request.");
		}

		await RefreshFriendActionButtonAsync();
		
	}
	
	private void OnDropdownSelected(long id)
	{
		if (_dropdownOptions == null)
			return;

		switch (id)
		{
			case 0:
				// messaging needs to be implemented
				
				var overlay = GetNode<ChatOverlay>("/root/ChatOverlay");
				overlay.OpenOverlay();
				overlay.OpenDm(profile.Username);
				overlay.GetNode<ChatManager>("/root/ChatManager")
					.MarkDmAsRead(profile.Username);
				break;
			case 1:
				var actionLabel = _dropdownOptions.GetItemText(_dropdownOptions.GetItemIndex(1));
				if (string.Equals(actionLabel, "Block", StringComparison.OrdinalIgnoreCase))
				{	
					BlockUser();
				}
				else if (string.Equals(actionLabel, "Unblock", StringComparison.OrdinalIgnoreCase))
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
		if (string.IsNullOrWhiteSpace(profile.UserId) || _dropdownOptions == null)
			return;

		GD.Print($"Attempting to block {profile.UserId}");
		_friendService.Block(profile.UserId);
		var blockIndex = _dropdownOptions.GetItemIndex(1);
		if (blockIndex >= 0)
		{
			_dropdownOptions.SetItemText(blockIndex, "Unblock");
			_dropdownOptions.SetItemId(blockIndex, 1);
		}
	}
	
	public void UnblockUser()
	{
		if (string.IsNullOrWhiteSpace(profile.UserId) || _dropdownOptions == null)
			return;

		GD.Print($"Attempting to unblock {profile.UserId}");
		_friendService.Unblock(profile.UserId);
		var blockIndex = _dropdownOptions.GetItemIndex(1);
		if (blockIndex >= 0)
		{
			_dropdownOptions.SetItemText(blockIndex, "Block");
			_dropdownOptions.SetItemId(blockIndex, 1);
		}
	}
	
	private void GoFriendsList() 
	{
		ResetUiNavigationState();
		var tree = GetTree();
		tree.SetMeta(FriendsListOwnerMeta, profile.Username ?? string.Empty);
		tree.SetMeta(FoundProfileUsernameMeta, profile.Username ?? string.Empty);
		tree.SetMeta(FoundProfileUserIdMeta, profile.UserId ?? string.Empty);
		tree.SetMeta("pgemu_return_scene", "res://FoundUserProfile.tscn");
		tree.ChangeSceneToFile("res://FriendsList.tscn");
	}

	private void GoAchievements()
	{
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(1);
		AchievementStorage.gameId = -1;
		AchievementStorage.gameName = "Showcase";
		AchievementStorage.achievementData = null;
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://FoundUserProfile.tscn");
		tree.ChangeSceneToFile("res://Achievements.tscn");
	}

	private void GoGameSelect()
	{
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://FoundUserProfile.tscn");
		tree.ChangeSceneToFile("res://GameSelect.tscn");
	}

	private void GoProfileSettings()
	{
		ResetUiNavigationState();
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.SetMeta(SettingsParentReturnSceneMeta, returnScene);
		tree.SetMeta("pgemu_return_scene", "res://profile.tscn");
		tree.SetMeta("pgemu_settings_tab", "profile");
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
				if (err != Error.Ok)
					err = avatar.LoadWebpFromBuffer(imageData);
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
		var candidates = new[]
		{
			System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "PGEmu.backend", relativePath)),
			System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "PGEmu.backend", "bin", "Debug", "net10.0", relativePath)),
			System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "..", "PGEmu.backend", "bin", "Release", "net10.0", relativePath))
		};

		foreach (var candidate in candidates)
		{
			if (System.IO.File.Exists(candidate))
				return candidate;
		}

		return candidates[0];
	}

	private sealed class ProfileGameSummary
	{
		public string ExternalGameId { get; set; } = string.Empty;
		public int PlaytimeMinutes { get; set; }
	}
	
}
