using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using PGEmu;
using PGEmu.app;
using PGEmu.Helpers;
using PGEmu.Services;
using PGEmu.Services.Models;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;

public partial class HomeScreen : Control
{
	// Scene wiring (assigned in `HomeScreen.tscn`).
	[Export] public NodePath SearchBarText;
	[Export] public NodePath SearchBarButton;
	[Export] public NodePath PrevPath;
	[Export] public NodePath NextPath;
	[Export] public NodePath SelectedTitlePath;
	[Export] public NodePath StatusPath;
	[Export] public NodePath SelectPlatformPath;
	[Export] public NodePath LogoutPath;
	[Export] public NodePath FriendsPath;
	[Export] public NodePath InboxPath;
	[Export] public NodePath MusicPath;
	[Export] public NodePath ChatPath;
	[Export] public NodePath SettingsPath;
	[Export] public NodePath HelpPath;
	[Export] public NodePath CollectionsPath;
	[Export] public NodePath CarouselAreaPath;
	
	// Friend Inbox popup
	[Export] public FriendInbox FriendInboxPopup;

	// New 3d carousel
	private ConsoleCarousel3DView _carousel = null!;
	
	private Button _prev;
	private Button _next;
	private Label _selectedTitle;
	private Label _status;
	private Button _selectPlatform;
	private Button _logout;
	private Button _friends;
	private Button _inbox;
	private Button _chat;
	private Button _settings;
	private Button _help;
	private Button _collections;
	private Button _music;
	private TextEdit _searchBarText;
	private Button _searchBarButton;
	private Control _carouselArea;
	
	private readonly List<PlatformConfig> _platforms = new();

	// Loaded from `config.json`
	private AppConfig? _config;
	private string? _configPath;
	
	// Gamepad navigation (left stick + d-pad)
	private const float AxisDeadzone = 0.55f;
	private const int AxisRepeatMs = 180;
	private const string SelectedPlatformMetaKey = "pgemu_selected_platform_id";
	private int _leftAxisDir;
	private long _leftAxisNextMs;
	private string? _rememberedPlatformId;
	private int _uiHorizontalAxisDir;
	private long _uiHorizontalAxisNextMs;
	private int _uiVerticalAxisDir;
	private long _uiVerticalAxisNextMs;
	private int _uiRowIndex = -1;
	private int _uiColumnIndex = -1;

	// for user search
	public ProfileService _profileService = new ProfileService();
	private readonly System.Net.Http.HttpClient _client = new();
	public ProfileService _profile = null!;
	
	
	private ScreenTransition Transition =>
		GetNode<ScreenTransition>("/root/ScreenTransition");
	

	public async override void _Ready()
	{
		// Resolve all node references up front; if a NodePath is wrong you'll fail here with a clear error.
		_prev = GetNode<Button>(PrevPath);
		_next = GetNode<Button>(NextPath);
		_selectedTitle = GetNode<Label>(SelectedTitlePath);
		_status = GetNode<Label>(StatusPath);
		_selectPlatform = GetNode<Button>(SelectPlatformPath);

		_searchBarText = GetNode<TextEdit>(SearchBarText);
		_searchBarButton = GetNode<Button>(SearchBarButton);


		_logout = GetNodeOrNull<Button>(LogoutPath);
		_inbox = GetNodeOrNull<Button>(InboxPath);
		_friends = GetNodeOrNull<Button>(FriendsPath);
		_chat = GetNodeOrNull<Button>(ChatPath);
		_settings = GetNodeOrNull<Button>(SettingsPath);
		_help = GetNodeOrNull<Button>(HelpPath);
		_collections = GetNodeOrNull<Button>(CollectionsPath);
		_music = GetNodeOrNull<Button>(MusicPath);
		
		ApplyAesthetic();

		// Build the 3D console carousel
		_carouselArea = GetNode<Control>(CarouselAreaPath);
		_carousel = new ConsoleCarousel3DView();
		_carousel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		
		// Put it behind the UI
		_carouselArea.AddChild(_carousel);
		MoveChild(_carousel, -1);

		_carousel.SelectionChanged += OnCarouselSelectionChanged;

		
		_prev.Pressed += () => Step(-1);
		_next.Pressed += () => Step(1);
		_selectPlatform.Pressed += OpenSelectedPlatform;

		if (_searchBarButton != null) _searchBarButton.Pressed += OnSearchBarPressed;
		if (_logout != null) _logout.Pressed += OnLogoutPressed;
		if (_settings != null) _settings.Pressed += OnSettingsPressed;
		if (_friends != null) _friends.Pressed += OnFriendsPressed;
		if (_chat != null) _chat.Pressed += OnChatPressed;
		if (_help != null) _help.Pressed += OnHelpPressed;
		if (_inbox != null) _inbox.Pressed += OnInboxPressed;
		if (_music != null) _music.Pressed += OnMusicPressed;
		if (_collections != null && !_collections.IsConnected(Button.SignalName.Pressed, Callable.From(OnCollectionsPressed)))
			_collections.Pressed += OnCollectionsPressed;
		
		// background transition
		StartBackgroundTransition();
		
		// Load platforms from config, then build the carousel visuals.
		ConnectAllButtons(this);
		InputRoutingService.Instance?.UnlockUiInput();
		ResetUiNavigationState();
		LoadConfigAndPlatforms();
		
		// Maps loaded platforms to console types
		var consoleTypes = _platforms.Select(p => p.Id.ToLower() switch
		{
			"wii"        => ConsoleCarousel3DView.ConsoleType.Wii,
			"ps2"        => ConsoleCarousel3DView.ConsoleType.PlayStation2,
			"psp"        => ConsoleCarousel3DView.ConsoleType.PSP,
			"gc"   => ConsoleCarousel3DView.ConsoleType.GameCube,
			"gba"        => ConsoleCarousel3DView.ConsoleType.GBA,
			_            => ConsoleCarousel3DView.ConsoleType.Wii // fallback
		}).ToList();

		// Populate carousel
		_carousel.Populate(consoleTypes);
		
		RestoreSelectedPlatformSelection();
		UpdateSelectedLabel();
		
		// we want to check if any emulator is running when posting the status
		// cause if they're meandering around the home menu but playing a game 
		// we don't want that to overtake this
		bool gameIsRunning = false;
		foreach (var emulator in PlaytimeStorage.EmulatorToName){
			
			Process[] runningEmulators = Process.GetProcessesByName(emulator.Value);
			if (runningEmulators.Length != 0){
				gameIsRunning = true;
				GD.Print(emulator.Value + " is running!");
				break;
			}
		}
		if (!gameIsRunning){
			ActivityManager.SetActivity("Playing", "In the Menus", "GodotClient");
			ActivityManager.runTimer();
		}
		
		
		await FriendActivity.GetFriendsJson();
		if (!IsScreenAlive())
			return;

		await FriendActivity.GetActivitiesAsync();
		if (!IsScreenAlive())
			return;

		SetupFriendHover();
	}
	
		private async void OnSearchBarPressed()
	{
		string? username = _searchBarText.Text;
		ProfileResponse profile = await _profileService.GetUserProfile(_searchBarText.Text?.Trim());
		if (profile != null)
		{
			Global.foundProfile = profile;
			GD.Print("Profile found:");
			GD.Print(profile.Username);
			var tree = GetTree();
			GD.Print(Global.foundProfile.UserId + Global.foundProfile.Username);
			tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
			tree.ChangeSceneToFile("res://FoundUserProfile.tscn");
		}

	}

	private void OnLogoutPressed()
	{
		var dialog = new ConfirmationDialog();
		dialog.DialogText = "Are you sure you want to log out?";
		AddChild(dialog);

		dialog.Confirmed += async () =>
		{
			AuthService.Instance.Logout();
			await Transition.ChangeScene("res://WelcomeScreen.tscn");
		};

		dialog.PopupCentered();
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
			bg.StartTransition("HomeScreen", 1.5f);
			GD.Print("Background transition finished!");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Gradient transition failed: {ex.Message}");
		}
		
	}
	
private void ConnectAllButtons(Node node)
{
	foreach (Node child in node.GetChildren())
	{
		if (child is Button button &&
			button != _prev &&
			button != _next &&
			button != _selectPlatform &&
			button != _settings &&
			button != _friends &&
			button != _inbox &&
			button != _collections)
		{
			button.Pressed += () =>
			{
				AudioManager.Instance?.PlayClick();
			};
		}

		ConnectAllButtons(child);
	}
}

	private bool IsScreenAlive()
	{
		return GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion() && IsInsideTree();
	}

	public void SetupFriendHover(){
		if (!IsScreenAlive())
			return;

		var friendsRow = GetNodeOrNull<Control>("Margin/Root/CenterArea/FriendsRow");
		if (friendsRow == null)
			return;

		foreach (Node child in friendsRow.GetChildren())
		{
			if (child is not Control control)
				continue;

			control.Visible = false;
			control.TooltipText = string.Empty;
		}

		if (FriendActivity.results.Count == 0)
		{
			GD.Print("Oops! Empty!");
			return;
		}

		List<KeyValuePair<string, KeyValuePair<string, DateTime>>> fullList = new();
		foreach (var friend in FriendActivity.results)
		{
			if (!TryReadFriendActivity(friend.Value, out var activity, out var updatedAt))
				continue;

			fullList.Add(new KeyValuePair<string, KeyValuePair<string, DateTime>>(
				friend.Key.Key,
				new KeyValuePair<string, DateTime>(activity, updatedAt)));
		}

		GD.Print("count of the list is " + fullList.Count);
		GD.Print(DateTime.UtcNow);

		for (int i = 0; i < Mathf.Min(3, fullList.Count); i++)
		{
			if (friendsRow.GetNodeOrNull<Control>($"Friend{i + 1}") is not Control friendSlot)
				continue;

			friendSlot.Visible = true;
			var statusText = BuildFriendStatusText(fullList[i].Value.Key, fullList[i].Value.Value);
			friendSlot.TooltipText = BuildFriendTooltip(fullList[i].Key, statusText);
		}

		if (fullList.Count > 3 && friendsRow.GetNodeOrNull<Label>("MoreFriends") is Label moreFriends)
		{
			moreFriends.Visible = true;
			moreFriends.Text = $"+{fullList.Count - 3} more";
		}
	}

	private static bool TryReadFriendActivity(JsonElement activityElement, out string activity, out DateTime updatedAt)
	{
		activity = "In the Menus";
		updatedAt = DateTime.UtcNow;

		if (activityElement.ValueKind != JsonValueKind.Object)
			return false;

		if (TryReadJsonString(activityElement, "ExternalGameId", out var externalGameId) && !string.IsNullOrWhiteSpace(externalGameId))
			activity = externalGameId;
		else if (TryReadJsonString(activityElement, "externalGameId", out externalGameId) && !string.IsNullOrWhiteSpace(externalGameId))
			activity = externalGameId;
		else if (TryReadJsonString(activityElement, "ActivityType", out var activityType) && !string.IsNullOrWhiteSpace(activityType))
			activity = activityType;
		else if (TryReadJsonString(activityElement, "activityType", out activityType) && !string.IsNullOrWhiteSpace(activityType))
			activity = activityType;

		if (TryReadJsonDateTime(activityElement, "UpdatedAt", out var parsedUpdatedAt) ||
			TryReadJsonDateTime(activityElement, "updatedAt", out parsedUpdatedAt))
		{
			updatedAt = parsedUpdatedAt;
		}

		return true;
	}

	private static bool TryReadJsonString(JsonElement element, string propertyName, out string value)
	{
		value = string.Empty;
		if (!element.TryGetProperty(propertyName, out var property))
			return false;

		switch (property.ValueKind)
		{
			case JsonValueKind.String:
				value = property.GetString() ?? string.Empty;
				return true;
			case JsonValueKind.Number:
			case JsonValueKind.True:
			case JsonValueKind.False:
				value = property.GetRawText();
				return true;
			default:
				return false;
		}
	}

	private static bool TryReadJsonDateTime(JsonElement element, string propertyName, out DateTime value)
	{
		value = default;
		if (!element.TryGetProperty(propertyName, out var property))
			return false;
		if (property.ValueKind != JsonValueKind.String)
			return false;

		return DateTime.TryParse(property.GetString(), out value);
	}

	private static string BuildFriendStatusText(string activity, DateTime updatedAt)
	{
		if ((DateTime.UtcNow - updatedAt).TotalMinutes > 5)
			return "Offline";

		return string.IsNullOrWhiteSpace(activity) ? "In the Menus" : activity;
	}

	private static string BuildFriendTooltip(string username, string statusText)
	{
		return username + "\n" + statusText;
	}
	
private void OnAnyButtonPressed()
{
	var audio = GetNode<AudioManager>("/root/AudioManager");
	audio.PlayClick();
}



	private void OnSettingsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		// Jump to the shared settings screen and return here afterward.
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.SetMeta("pgemu_settings_tab", "appearance");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);
		tree.ChangeSceneToFile("res://Settings.tscn");
	}

	private void OnFriendsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://profile.tscn");
	}
	
	private void OnMusicPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://Jukebox.tscn");
	}
	
	private void OnAchPressed(){
		AudioManager.Instance?.PlayNavigation(1);
		
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://Achievements.tscn");
	
	
	}
	
	private void OnCollectionsPressed(){
		AudioManager.Instance?.PlayNavigation(1);
		
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://Collections.tscn");
		
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

	private void OpenSelectedPlatform()
	{
		if (_platforms.Count == 0) return;

		var idx = Mathf.RoundToInt(_carousel.CarouselPos);
		idx = WrapIndex(idx);

		if (idx < 0 || idx >= _platforms.Count) return;
		var platform = _platforms[idx];
		AudioManager.Instance?.PlaySelect();

		// Pass selection to the next screen without needing a singleton.
		var tree = GetTree();
		tree.SetMeta(SelectedPlatformMetaKey, platform.Id);
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);

		tree.ChangeSceneToFile("res://GameSelect.tscn");
	}

	private void RestoreSelectedPlatformSelection()
	{
		if (Count == 0)
			return;

		var tree = GetTree();
		if (!tree.HasMeta(SelectedPlatformMetaKey))
			return;

		var platformId = tree.GetMeta(SelectedPlatformMetaKey).AsString();
		if (string.IsNullOrWhiteSpace(platformId))
			return;

		for (var i = 0; i < _platforms.Count; i++)
		{
			if (!string.Equals(_platforms[i].Id, platformId, StringComparison.OrdinalIgnoreCase))
				continue;

			_carousel.CarouselPos = i;
			_rememberedPlatformId = _platforms[i].Id;
			return;
		}
	}

	private void RememberSelectedPlatformSelection(int idx)
	{
		if (idx < 0 || idx >= _platforms.Count)
			return;

		var platformId = _platforms[idx].Id;
		if (string.IsNullOrWhiteSpace(platformId))
			return;

		if (string.Equals(_rememberedPlatformId, platformId, StringComparison.OrdinalIgnoreCase))
			return;

		GetTree().SetMeta(SelectedPlatformMetaKey, platformId);
		_rememberedPlatformId = platformId;
	}
	
	private void OnCarouselSelectionChanged(int index)
	{
		if (index < 0 || index >= _platforms.Count) return;
		_selectedTitle.Text = _platforms[index].Name;
		RememberSelectedPlatformSelection(index);
	}
	

	private int Count => _platforms.Count;

	private int WrapIndex(int i)
	{
		// Wrap into [0, Count).
		if (Count == 0) return 0;
		i %= Count;
		if (i < 0) i += Count;
		return i;
	}

	private float WrapPos(float p)
	{
		if (Count == 0) return 0f;
		// Keep in [0, Count).
		p %= Count;
		if (p < 0) p += Count;
		return p;
	}

	private void Step(int dir)
	{
		if (_platforms.Count <= 1) return;
		AudioManager.Instance?.PlayNavigation(dir);
		_carousel.StepDirection(dir);
		
	}
	

	public override void _Process(double delta)
	{
		// Keep layout in sync while tweening and while `_carouselPos` is updated by dragging.
		UpdateSelectedLabel();
		
	}



	public override void _UnhandledInput(InputEvent e)
	{
		if (ShouldIgnoreUiInput()) return;

		if (e is InputEventJoypadMotion jm)
		{
			if (!ShouldHandleControllerInput(jm.Device))
				return;

			if (HandleControllerUiAxis(jm))
			{
				MarkInputHandled();
				return;
			}

			if (!IsUiNavigationActive() && Count > 1 && HandleAxisNav(jm))
			{
				MarkInputHandled();
			}
			return;
		}

		if (e is not InputEventJoypadButton jb || !jb.Pressed)
			return;

		if (!ShouldHandleControllerInput(jb.Device))
			return;

		if (HandleControllerUiButton(jb.ButtonIndex))
		{
			MarkInputHandled();
			return;
		}

		switch (jb.ButtonIndex)
		{
			case JoyButton.LeftShoulder:
			case JoyButton.DpadLeft:
				if (Count > 1)
				{
					Step(-1);
					MarkInputHandled();
				}
				break;
			case JoyButton.RightShoulder:
			case JoyButton.DpadRight:
				if (Count > 1)
				{
					Step(1);
					MarkInputHandled();
				}
				break;
			case JoyButton.A:
				MarkInputHandled();
				OpenSelectedPlatform();
				break;
			case JoyButton.Start:
				if (_settings != null)
				{
					MarkInputHandled();
					OnSettingsPressed();
				}
				break;
			case JoyButton.Touchpad:
				if (_friends != null)
				{
					MarkInputHandled();
					OnFriendsPressed();
				}
				break;
			case JoyButton.Guide:
				MarkInputHandled();
				GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
				break;
		}
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
					_uiColumnIndex = Mathf.Min(1, rows[0].Count - 1);
					return ControllerService.FocusRowEntry(rows, ref _uiRowIndex, ref _uiColumnIndex);
				case JoyButton.DpadDown:
					_uiRowIndex = rows.Count - 1;
					_uiColumnIndex = Mathf.Min(1, rows[_uiRowIndex].Count - 1);
					return ControllerService.FocusRowEntry(rows, ref _uiRowIndex, ref _uiColumnIndex);
			}

			return false;
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
				if (ControllerService.IsConfirmButton(button))
					return ControllerService.ActivateRowSelection(rows, _uiRowIndex, _uiColumnIndex);
				if (ControllerService.IsBackButton(button))
				{
					ExitUiNavigation();
					return true;
				}
				return false;
		}
	}

	private bool HandleControllerUiAxis(InputEventJoypadMotion jm)
	{
		var rows = GetControllerUiRows();
		if (rows.Count == 0)
			return false;

		if (jm.Axis == JoyAxis.LeftY)
		{
			if (!IsUiNavigationActive())
			{
				return ControllerService.TryHandleMenuAxis(jm.AxisValue, ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs, dir =>
				{
					_uiRowIndex = dir < 0 ? 0 : rows.Count - 1;
					_uiColumnIndex = Mathf.Min(1, rows[_uiRowIndex].Count - 1);
					ControllerService.FocusRowEntry(rows, ref _uiRowIndex, ref _uiColumnIndex);
				});
			}

			return ControllerService.TryHandleMenuAxis(jm.AxisValue, ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs, dir =>
			{
				ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, dir, 0);
			});
		}

		if (!IsUiNavigationActive() || jm.Axis != JoyAxis.LeftX)
			return false;

		return ControllerService.TryHandleMenuAxis(jm.AxisValue, ref _uiHorizontalAxisDir, ref _uiHorizontalAxisNextMs, dir =>
		{
			ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, 0, dir);
		});
	}

	private List<List<Button>> GetControllerUiRows()
	{
		return ControllerService.BuildVisibleRows(
			new[] { _logout, _searchBarButton, _inbox, _collections, _friends, _chat, _settings, _help },
			new[] { _prev, _selectPlatform, _next });
	}

	private bool IsUiNavigationActive()
	{
		return _uiRowIndex >= 0 && _uiColumnIndex >= 0;
	}

	private void ExitUiNavigation()
	{
		if (GetViewport()?.GuiGetFocusOwner() is Control focused)
			focused.ReleaseFocus();

		ResetUiNavigationState();
	}

	private void ResetUiNavigationState()
	{
		_uiRowIndex = -1;
		_uiColumnIndex = -1;
		ControllerService.ResetMenuAxis(ref _uiHorizontalAxisDir, ref _uiHorizontalAxisNextMs);
		ControllerService.ResetMenuAxis(ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs);
	}

	private void MarkInputHandled()
	{
		GetViewport()?.SetInputAsHandled();
	}

	private bool HandleAxisNav(InputEventJoypadMotion jm)
	{
		if (jm.Axis == JoyAxis.LeftX)
			return HandleAxis(jm.AxisValue, ref _leftAxisDir, ref _leftAxisNextMs);

		return false;
	}

	private bool HandleAxis(float value, ref int heldDir, ref long nextMs)
	{
		return ControllerService.TryHandleMenuAxis(value, ref heldDir, ref nextMs, Step);
	}
	
	private void UpdateSelectedLabel()
	{
		if (_selectedTitle == null) return;
		if (Count == 0) return;

		// Treat the rounded position as "selected".
		var idx = Mathf.RoundToInt(_carousel.CarouselPos);
		idx = WrapIndex(idx);

		if (idx >= 0 && idx < _platforms.Count)
		{
			_selectedTitle.Text = _platforms[idx].Name;
			if (_status != null)
				_status.Text = _configPath != null
					? $"{_platforms[idx].Name} selected (loaded {_configPath})"
					: $"{_platforms[idx].Name} selected";
			RememberSelectedPlatformSelection(idx);
		}
	}

	private void UpdateNavEnabled()
	{
		var enabled = Count > 1;
		if (_prev != null) _prev.Disabled = !enabled;
		if (_next != null) _next.Disabled = !enabled;
	}

	private void LoadConfigAndPlatforms()
	{
		try
		{
			_configPath = ConfigFinder.FindConfigPath();

			// 2) Godot-friendly fallback: look relative to the project root.
			_configPath ??= TryFindConfigNearGodotProject();

			if (_configPath == null)
			{
				SetStatus("config.json not found. Put it in the repo root or inside the Godot project folder.");
				_config = null;
				return;
			}

			_config = AppConfig.Load(_configPath);
			// Your sample config uses `~/` for LibraryRoot; .NET doesn't auto-expand that.
			_config.LibraryRoot = ExpandHomePath(_config.LibraryRoot);
			PlatformList._configuration = _config;

			SetStatus($"Loaded config: {_configPath}");
		}
		catch (Exception ex)
		{
			_config = null;
			_configPath = null;
			SetStatus($"Config load failed: {ex.Message}");
		}
		
		// Load platforms
		_platforms.Clear();
		if (_config?.Platforms is { Count: > 0 } platforms)
		{
			_platforms.AddRange(platforms);
			PlatformList.platformList = platforms;
		}
		else
		{
			_platforms.Add(new PlatformConfig { Id = "missing", Name = "Missing config.json" });
		}

		UpdateNavEnabled();
	}

	private static string? TryFindConfigNearGodotProject()
	{
		try
		{
			// `res://` is the project root; GlobalizePath gives an OS path.
			var projectDir = ProjectSettings.GlobalizePath("res://");

			var inProject = Path.Combine(projectDir, "config.json");
			if (File.Exists(inProject)) return inProject;

			var inParent = Path.GetFullPath(Path.Combine(projectDir, "..", "config.json"));
			if (File.Exists(inParent)) return inParent;
		}
		catch
		{
			// Best-effort; ignore.
		}

		return null;
	}

	private static string ExpandHomePath(string path)
	{
		// Expand "~" and "~/..." into an absolute path. (On Windows, "~" isn't typically used, but this helps macOS/Linux.)
		if (string.IsNullOrWhiteSpace(path)) return path;

		if (path == "~")
			return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

		if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
		{
			var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
			var rest = path.Substring(2);
			return Path.Combine(home, rest);
		}

		return path;
	}

	private void SetStatus(string text)
	{
		if (_status != null)
			_status.Text = text;
		else
			GD.Print(text);
	}

	private bool ShouldIgnoreUiInput()
	{
		return InputRoutingService.Instance?.IsUiInputBlocked == true;
	}

	private static bool ShouldHandleControllerInput(int device)
	{
		return ControllerService.Instance?.ShouldHandleMenuInput(device) ?? true;
	}

	private void ApplyAesthetic()
	{
		// Keep all home controls on the same visual language as the dark launcher theme.
		// Nav buttons
		UiStyle.StyleNavButton(_prev);
		UiStyle.StyleNavButton(_next);
		UiStyle.StyleGhostNav(_prev, _next);

		// Primary actions
		UiStyle.StylePrimaryButton(_selectPlatform);
		UiStyle.AddHoverFeedback(_selectPlatform);
		UiStyle.ApplyDropShadow(_selectPlatform);

		// Top bar buttons
		UiStyle.StyleTopBarButton(_logout);
		UiStyle.AddHoverFeedback(_logout);
		UiStyle.ApplyDropShadow(_logout);

		UiStyle.StyleTopBarButton(_inbox);
		UiStyle.AddHoverFeedback(_inbox);
		UiStyle.ApplyDropShadow(_inbox);

		UiStyle.StyleTopBarButton(_friends);
		UiStyle.AddHoverFeedback(_friends);
		UiStyle.ApplyDropShadow(_friends);

		UiStyle.StyleTopBarButton(_chat);
		UiStyle.AddHoverFeedback(_chat);
		UiStyle.ApplyDropShadow(_chat);

		UiStyle.StyleTopBarButton(_settings);
		UiStyle.AddHoverFeedback(_settings);
		UiStyle.ApplyDropShadow(_settings);

		UiStyle.StyleTopBarButton(_help);
		UiStyle.AddHoverFeedback(_help);
		UiStyle.ApplyDropShadow(_help);

		UiStyle.StyleTopBarButton(_collections);
		UiStyle.AddHoverFeedback(_collections);
		UiStyle.ApplyDropShadow(_collections);

		// Labels
		UiStyle.StyleTitleLabel(_selectedTitle);
		UiStyle.StyleStatusLabel(_status);
		
	}
}
