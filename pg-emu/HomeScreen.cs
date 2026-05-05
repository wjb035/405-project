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
using System.Threading.Tasks;

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
	[Export] public NodePath SearchFilterPath;
	
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
	private Button _filter;
	private TextEdit _searchBarText;
	private Button _searchBarButton;
	private Control _carouselArea;
	private PopupPanel _userSearchResultsPopup = null!;
	private HelpPopup _helpPopup = null!;
	private Panel _userSearchResultsContent = null!;
	private ScrollContainer _userSearchResultsScroll = null!;
	private VBoxContainer _userSearchResultsList = null!;
	private readonly List<string> _userSearchResultUsernames = new();
	private readonly List<Button> _userSearchResultButtons = new();
	private int _selectedUserSearchResultIndex = -1;
	
	private readonly List<PlatformConfig> _platforms = new();

	// Loaded from `config.json`
	private AppConfig? _config;
	private string? _configPath;
	
	// Gamepad navigation (left stick + d-pad)
	private const float AxisDeadzone = 0.55f;
	private const int AxisRepeatMs = 180;
	private const string SelectedPlatformMetaKey = "pgemu_selected_platform_id";
	private const string GbaReturnEjectMetaKey = "pgemu_gba_return_eject_on_home";
	private int _leftAxisDir;
	private long _leftAxisNextMs;
	private string? _rememberedPlatformId;
	private int _uiHorizontalAxisDir;
	private long _uiHorizontalAxisNextMs;
	private int _uiVerticalAxisDir;
	private long _uiVerticalAxisNextMs;
	private int _uiRowIndex = -1;
	private int _uiColumnIndex = -1;
	private string filterType = "Games";


	// for user search
	public ProfileService _profileService = new ProfileService();
	private readonly System.Net.Http.HttpClient _client = new();
	public ProfileService _profile = null!;
	private int _userSearchRequestId;
	
	
	private ScreenTransition Transition =>
		GetNode<ScreenTransition>("/root/ScreenTransition");
	
	private List<String> AllGames = new();
	public async override void _Ready()
	{
		var chatManager = GetNode<ChatManager>("/root/ChatManager");

		if (!chatManager.IsConnected && AuthService.Instance.IsLoggedIn())
		{
			chatManager.Username = AuthService.Instance.Username;
			GD.Print("Connecting chat as: " + chatManager.Username);
			chatManager.ConnectToChat();
		}
		else
		{
			GD.Print("Chat NOT connecting.. IsConnected: " + chatManager.IsConnected + " IsLoggedIn: " + AuthService.Instance.IsLoggedIn());
		}
		
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
		_filter = GetNodeOrNull<Button>(SearchFilterPath);
		ApplyAesthetic();
		SetupUserSearchResultsPopup();
		SetupHelpPopup();

		// Build the 3D console carousel
		_carouselArea = GetNode<Control>(CarouselAreaPath);
		_carousel = new ConsoleCarousel3DView();
		_carousel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_carousel.OffsetLeft = -10f;
		
		// Put it behind the UI inside the carousel container.
		_carouselArea.AddChild(_carousel);
		_carouselArea.MoveChild(_carousel, -1);

		_carousel.SelectionChanged += OnCarouselSelectionChanged;

		
		_prev.Pressed += () => Step(-1);
		_next.Pressed += () => Step(1);
		_selectPlatform.Pressed += OpenSelectedPlatform;

		if (_searchBarButton != null) _searchBarButton.Pressed += OnSearchBarPressed;
		if (_searchBarText != null)
		{
			_searchBarText.TextChanged += OnSearchBarTextChanged;
			_searchBarText.GuiInput += OnSearchBarGuiInput;
		}
		if (_logout != null) _logout.Pressed += OnLogoutPressed;
		if (_settings != null) _settings.Pressed += OnSettingsPressed;
		if (_friends != null) _friends.Pressed += OnFriendsPressed;
		if (_chat != null) _chat.Pressed += OnChatPressed;
		if (_help != null) _help.Pressed += OnHelpPressed;
		if (_inbox != null) _inbox.Pressed += OnInboxPressed;
		if (_music != null) _music.Pressed += OnMusicPressed;
		if (_filter != null) _filter.Pressed += OnFilterPressed;
		if (_collections != null && !_collections.IsConnected(Button.SignalName.Pressed, Callable.From(OnCollectionsPressed)))
			_collections.Pressed += OnCollectionsPressed;
		
		// background transition
		StartBackgroundTransition();
		
		// For testing
		var chatOverlay = GetNode<ChatOverlay>("/root/ChatOverlay");
		chatOverlay._isLoggedOut = false;
		
		// Load platforms from config, then build the carousel visuals.
		ConnectAllButtons(this);
		InputRoutingService.Instance?.UnlockUiInput();
		ResetUiNavigationState();
		LoadConfigAndPlatforms();
		BackgroundArtCache.Instance?.Begin(_config, _configPath);
		
		// Maps loaded platforms to console types
		var consoleTypes = _platforms.Select(p => p.Id.ToLower() switch
		{
			"wii"        => ConsoleCarousel3DView.ConsoleType.Wii,
			"ds"         => ConsoleCarousel3DView.ConsoleType.NintendoDS,
			"n64"        => ConsoleCarousel3DView.ConsoleType.Nintendo64,
			"snes"       => ConsoleCarousel3DView.ConsoleType.SNES,
			"nes"        => ConsoleCarousel3DView.ConsoleType.NES,
			"ps1"        => ConsoleCarousel3DView.ConsoleType.PlayStation1,
			"ps2"        => ConsoleCarousel3DView.ConsoleType.PlayStation2,
			"psp"        => ConsoleCarousel3DView.ConsoleType.PSP,
			"gc"   => ConsoleCarousel3DView.ConsoleType.GameCube,
			"gba"        => ConsoleCarousel3DView.ConsoleType.GBA,
			_            => ConsoleCarousel3DView.ConsoleType.Wii // fallback
		}).ToList();

		foreach (var p in _platforms){
			GD.Print("For the search function: " + p.Name);	
			var scanned = LibraryScanner.Scan(p, _config.LibraryRoot, out var scanDir);
			foreach (var g in scanned){
				AllGames.Add(g.Title);
			}
		}
		
		
		// Populate carousel
		_carousel.Populate(consoleTypes);
		
		RestoreSelectedPlatform();
		UpdateSelectedLabel();
		StartSelectedPlatformCoverArtWarmup();
		
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
		await TriggerUserSearchFromInputAsync();
	}
	private void OnFilterPressed(){
		if (filterType == "Both"){
			_searchBarText.PlaceholderText = "Search Games";
			_filter.Text = "Games";
			filterType = "Games";
			
		}
		else if (filterType == "Games"){
			_searchBarText.PlaceholderText = "Search Users";
			_filter.Text = "Users";
			filterType = "Users";
		}
		else if (filterType == "Users"){
			_searchBarText.PlaceholderText = "Search everything";
			_filter.Text = "Both";
			filterType = "Both";
		}
		
	}	
		
		
	private void SetupUserSearchResultsPopup()
	{
		_userSearchResultsPopup = new PopupPanel
		{
			Visible = false,
			Unfocusable = true
		};
		_userSearchResultsPopup.Hide();
		_userSearchResultsPopup.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.10f, 0.20f, 0.96f),
			BorderColor = new Color(0.66f, 0.55f, 0.86f, 0.90f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10
		});

		_userSearchResultsContent = new Panel
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_userSearchResultsContent.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_userSearchResultsContent.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.15f, 0.12f, 0.24f, 0.97f),
			BorderColor = new Color(0.72f, 0.62f, 0.92f, 0.88f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10
		});

		_userSearchResultsScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_userSearchResultsScroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_userSearchResultsScroll.OffsetLeft = 8;
		_userSearchResultsScroll.OffsetTop = 8;
		_userSearchResultsScroll.OffsetRight = -8;
		_userSearchResultsScroll.OffsetBottom = -8;
		_userSearchResultsContent.AddChild(_userSearchResultsScroll);

		_userSearchResultsList = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		_userSearchResultsList.AddThemeConstantOverride("separation", 4);
		_userSearchResultsScroll.AddChild(_userSearchResultsList);

		_userSearchResultsPopup.AddChild(_userSearchResultsContent);
		AddChild(_userSearchResultsPopup);
	}

	private void OnSearchBarTextChanged()
	{
		var currentText = _searchBarText.Text ?? string.Empty;
		if (currentText.Contains('\n') || currentText.Contains('\r'))
		{
			_searchBarText.Text = NormalizeSearchInput(currentText);
			_ = TriggerUserSearchFromInputAsync();
			return;
		}

		_ = UpdateUserSearchResultsAsync(_searchBarText.Text);
	}

	private void OnSearchBarGuiInput(InputEvent @event)
	{
		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
			return;

		switch (keyEvent.Keycode)
		{
			case Key.Down:
				if (_userSearchResultsPopup.Visible)
				{
					MoveUserSearchSelection(1);
					AcceptEvent();
				}
				return;
			case Key.Up:
				if (_userSearchResultsPopup.Visible)
				{
					MoveUserSearchSelection(-1);
					AcceptEvent();
				}
				return;
			case Key.Escape:
				HideUserSearchResultsPopup();
				return;
			case Key.Enter:
			case Key.KpEnter:
				AcceptEvent();
				_ = TriggerUserSearchFromInputAsync();
				return;
		}
	}

	private async Task TriggerUserSearchFromInputAsync()
	{
		var username = ResolveUserSearchUsername();
		if (string.IsNullOrWhiteSpace(username))
			return;

		HideUserSearchResultsPopup();
		await OpenUserProfileByUsernameAsync(username);
	}

	private string ResolveUserSearchUsername()
	{
		if (_userSearchResultsPopup.Visible &&
			_selectedUserSearchResultIndex >= 0 &&
			_selectedUserSearchResultIndex < _userSearchResultUsernames.Count)
			return _userSearchResultUsernames[_selectedUserSearchResultIndex];

		return NormalizeSearchInput(_searchBarText.Text);
	}


	// THIS seems to be the functionality for the searching 
	private async Task UpdateUserSearchResultsAsync(string? query)
	{
		var search = NormalizeSearchInput(query);
		if (string.IsNullOrWhiteSpace(search))
		{
			HideUserSearchResultsPopup();
			return;
		}

		var requestId = ++_userSearchRequestId;
		// MUST UNCOMMENT THIS LATER
		var similarUsers = await _profileService.SearchUsersBySimilarity(search, 10);
		var games = (await SearchGamesBySimilarity(search, 10)).ToList();
		//games = games.ToList();
		if (requestId != _userSearchRequestId || !IsScreenAlive())
			return;

		var usernames = similarUsers
			.Select(user => NormalizeSearchInput(user.Username))
			.Where(username => !string.IsNullOrWhiteSpace(username))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Cast<string>();
		
		// using a dictionary to sort out the functionality
			
		Dictionary<string, string> UsersAndGames = new();
		if (filterType == "Both"){
			foreach (var g in games){
				UsersAndGames.Add(g,"game");
			}
			foreach (var u in usernames){
				UsersAndGames.Add(u,"user");
			}
		}
		else if (filterType == "Users"){
			foreach (var u in usernames){
				UsersAndGames.Add(u,"user");
			}
		}
		else if (filterType == "Games"){
			foreach (var g in games){
			UsersAndGames.Add(g,"game");
			}
		}
		
		games.AddRange(usernames);
		PopulateUserSearchResults(UsersAndGames);
	}

	public async Task<IReadOnlyList<String>> SearchGamesBySimilarity(string? query, int limit = 12)
	{
		var trimmedQuery = query?.Trim();
		if (string.IsNullOrWhiteSpace(trimmedQuery))
			return Array.Empty<String>();

		var clampedLimit = Math.Clamp(limit, 1, 25);
		var results = AllGames
		.Where(s => !string.IsNullOrWhiteSpace(s) &&
					s.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
		.Take(clampedLimit)
		.ToList();
		return results;
	}



	private void PopulateUserSearchResults(Dictionary<string, string> usernames)
	{
		foreach (Node child in _userSearchResultsList.GetChildren().ToArray())
			child.QueueFree();

		_userSearchResultUsernames.Clear();
		_userSearchResultButtons.Clear();
		_selectedUserSearchResultIndex = -1;

		foreach (var username in usernames.Take(10))
		//foreach (var username in AllGames.Take(10))
		{
			var trimmedUsername = NormalizeSearchInput(username.Key);
			if (string.IsNullOrWhiteSpace(trimmedUsername))
				continue;

			var index = _userSearchResultUsernames.Count;
			_userSearchResultUsernames.Add(trimmedUsername);
			var itemButton = CreateUserSearchResultButton(trimmedUsername, index);
			if (username.Value == "game"){
				itemButton = CreateGameSearchResultButton(trimmedUsername, index);
			}
			
			_userSearchResultButtons.Add(itemButton);
			_userSearchResultsList.AddChild(itemButton);
			if (username.Value == "user"){
				itemButton.Modulate = new Color(1, 1, 1, 0);
			}
			else{
				itemButton.Modulate = new Color(1, 150, 1, 0);
			}
		//	itemButton.Modulate = new Color(1, 1, 1, 0);
			var tween = CreateTween();
			tween.TweenProperty(itemButton, "modulate:a", 1f, 0.08f);
		}

		if (_userSearchResultUsernames.Count == 0)
		{
			HideUserSearchResultsPopup();
			return;
		}

		SetSelectedUserSearchResult(0, updateSearchField: false);
		ShowUserSearchResultsPopup();
	}

	private Button CreateGameSearchResultButton(string username, int index)
	{
		var button = new Button
		{
			Text = username,
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
			CustomMinimumSize = new Vector2(0, 32),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			ToggleMode = true,
			FocusMode = Control.FocusModeEnum.None
		};
		UiStyle.StyleTopBarButton(button);
		UiStyle.TightenButtonContentPadding(button, horizontal: 8f, vertical: 4f);

		button.MouseEntered += () => SetSelectedUserSearchResult(index, updateSearchField: true);
		button.Pressed += () => _ =  OnGameSearchResultPressedAsync(username);
		//button.Pressed += () => GD.Print("you separated them correctly!");
		return button;
	}

	private async Task OnGameSearchResultPressedAsync(string name)
	{
		

		GD.Print(name);
		var platformMatched = _platforms[0];
		foreach (var p in _platforms){
			var scanned = LibraryScanner.Scan(p, _config.LibraryRoot, out var scanDir);
			foreach (var g in scanned){
				if (g.Title == name){
					platformMatched = p;
					break;
				}
			}
		}
		
		if (platformMatched != null){
			var tree = GetTree();
			tree.SetMeta(SelectedPlatformMetaKey, platformMatched.Id);
			if (_configPath != null)
				tree.SetMeta("pgemu_config_path", _configPath);
			//using this to just store how we sort by games;
			CollectionStorage.SearchResult = name;
			await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Spiral, 0.35f, 0f);
		}
		else{
			GD.Print("Error getting the platform!");
		}
		/*// Pass selection to the next screen without needing a singleton.
		
		*/
	}
	
	
	private void ShowUserSearchResultsPopup()
	{
		if (_userSearchResultUsernames.Count == 0)
			return;

		var shouldKeepSearchFocus = _searchBarText.HasFocus();
		var textRect = _searchBarText.GetGlobalRect();
		var buttonRect = _searchBarButton.GetGlobalRect();

		var minX = Mathf.Min(textRect.Position.X, buttonRect.Position.X);
		var maxX = Mathf.Max(textRect.End.X, buttonRect.End.X);
		var popupWidth = Math.Max(260, (int)Mathf.Ceil(maxX - minX));
		var popupHeight = Mathf.Clamp((_userSearchResultUsernames.Count * 38) + 14, 74, 260);
		var popupY = (int)Mathf.Ceil(textRect.End.Y + 4f);
		var popupRect = new Rect2I((int)Mathf.Floor(minX), popupY, popupWidth, popupHeight);

		if (_userSearchResultsPopup.Visible)
		{
			_userSearchResultsPopup.Position = popupRect.Position;
			_userSearchResultsPopup.Size = popupRect.Size;
		}
		else
		{
			_userSearchResultsPopup.Popup(popupRect);
		}

		if (shouldKeepSearchFocus)
			CallDeferred(nameof(RestoreSearchInputFocus));
	}

	private void HideUserSearchResultsPopup()
	{
		_selectedUserSearchResultIndex = -1;
		foreach (var button in _userSearchResultButtons)
			button.SetPressedNoSignal(false);

		if (GodotObject.IsInstanceValid(_userSearchResultsPopup) && _userSearchResultsPopup.Visible)
			_userSearchResultsPopup.Hide();
	}

	private void RestoreSearchInputFocus()
	{
		if (!GodotObject.IsInstanceValid(_searchBarText) || !_searchBarText.IsInsideTree())
			return;

		_searchBarText.GrabFocus();
		_searchBarText.SetCaretColumn((_searchBarText.Text ?? string.Empty).Length);
	}

	private Button CreateUserSearchResultButton(string username, int index)
	{
		var button = new Button
		{
			Text = username,
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
			CustomMinimumSize = new Vector2(0, 32),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			ToggleMode = true,
			FocusMode = Control.FocusModeEnum.None
		};
		UiStyle.StyleTopBarButton(button);
		UiStyle.TightenButtonContentPadding(button, horizontal: 8f, vertical: 4f);

		button.MouseEntered += () => SetSelectedUserSearchResult(index, updateSearchField: true);
		button.Pressed += () => _ = OnUserSearchResultPressedAsync(index);
		return button;
	}

	private void SetSelectedUserSearchResult(int index, bool updateSearchField)
	{
		if (index < 0 || index >= _userSearchResultUsernames.Count || index >= _userSearchResultButtons.Count)
			return;

		_selectedUserSearchResultIndex = index;
		for (int i = 0; i < _userSearchResultButtons.Count; i++)
			_userSearchResultButtons[i].SetPressedNoSignal(i == index);

		if (!updateSearchField)
			return;

		var username = _userSearchResultUsernames[index];
		_searchBarText.Text = username;
		_searchBarText.SetCaretColumn(username.Length);
	}

	private void MoveUserSearchSelection(int direction)
	{
		if (_userSearchResultUsernames.Count == 0)
			return;

		var nextIndex = _selectedUserSearchResultIndex;
		if (nextIndex < 0)
			nextIndex = 0;
		else
			nextIndex = Mathf.Wrap(nextIndex + direction, 0, _userSearchResultUsernames.Count);

		SetSelectedUserSearchResult(nextIndex, updateSearchField: true);
	}

	private async Task OnUserSearchResultPressedAsync(int index)
	{
		if (index < 0 || index >= _userSearchResultUsernames.Count)
			return;

		SetSelectedUserSearchResult(index, updateSearchField: true);
		var username = _userSearchResultUsernames[index];
		HideUserSearchResultsPopup();
		await OpenUserProfileByUsernameAsync(username);
	}

	private async Task<bool> OpenUserProfileByUsernameAsync(string username)
	{
		if (string.IsNullOrWhiteSpace(username))
			return false;

		var profile = await _profileService.GetUserProfile(username.Trim());
		if (profile == null)
			return false;

		Global.foundProfile = profile;
		var tree = GetTree();
		tree.SetMeta("pgemu_found_profile_username", profile.Username ?? string.Empty);
		tree.SetMeta("pgemu_found_profile_user_id", profile.UserId ?? string.Empty);
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		
		await Transition.ChangeScene("res://FoundUserProfile.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
		return true;
	}

	private static string NormalizeSearchInput(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return string.Empty;

		return text.Replace("\r", " ").Replace("\n", " ").Trim();
	}

	private void OnLogoutPressed()
	{
		var dialog = new ConfirmationDialog();
		dialog.DialogText = "Are you sure you want to log out?";
		AddChild(dialog);

		dialog.Confirmed += async () =>
		{
			AuthService.Instance.Logout();
			var chatOverlay = GetNode<ChatOverlay>("/root/ChatOverlay");
			chatOverlay._isLoggedOut = true;
			chatOverlay.CloseOverlay();

			await Transition.ChangeScene("res://WelcomeScreen.tscn", ScreenTransition.TransitionType.Radial, 1f, 0.5f);

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

		var friendsRow = GetNodeOrNull<Control>("Margin/Root/CenterArea/Foreground2/FriendsRow");
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



	private async void OnSettingsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		// Jump to the shared settings screen and return here afterward.
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.SetMeta("pgemu_settings_tab", "appearance");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);
		await Transition.ChangeScene("res://Settings.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
	}

	private async void OnFriendsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		await Transition.ChangeScene("res://profile.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);

	}
	
	private async void OnMusicPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		await Transition.ChangeScene("res://Jukebox.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
	}
	
	private void OnAchPressed(){
		AudioManager.Instance?.PlayNavigation(1);
		
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://Achievements.tscn");
	
	
	}
	
	private async void OnCollectionsPressed(){
		AudioManager.Instance?.PlayNavigation(1);
		
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		await Transition.ChangeScene("res://Collections.tscn", ScreenTransition.TransitionType.Noise, 0.5f, 0.15f, true);

	}
	
	private void OnInboxPressed()
	{
		FriendInboxPopup.ShowPopup();
	}

	private void OnChatPressed()
	{
		var overlay = GetNode<ChatOverlay>("/root/ChatOverlay");
		overlay.ToggleOverlay();
	}

	private void OnHelpPressed()
	{
		_helpPopup.ShowPopup();
	}

	private void SetupHelpPopup()
	{
		_helpPopup = new HelpPopup();
		AddChild(_helpPopup);
	}

	private async void OpenSelectedPlatform()
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

		StartCoverArtWarmup(platform);
		if (string.Equals(platform.Id, "wii", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.Wii);
		else if (string.Equals(platform.Id, "n64", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.Nintendo64);
		else if (string.Equals(platform.Id, "snes", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.SNES);
		else if (string.Equals(platform.Id, "nes", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.NES);
		else if (string.Equals(platform.Id, "gc", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.GameCube);
		else if (string.Equals(platform.Id, "gba", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.GBA);
		else if (string.Equals(platform.Id, "ps1", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.PlayStation1);
		else if (string.Equals(platform.Id, "ps2", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.PlayStation2);
		else if (string.Equals(platform.Id, "psp", StringComparison.OrdinalIgnoreCase))
			await _carousel.PlaySelectedConsoleAnimationAsync(ConsoleCarousel3DView.ConsoleType.PSP);

		const float platformTransitionDuration = 0.68f;
		const float platformTransitionHold = 0.14f;
		_carousel.StartSelectionCameraPush(platformTransitionDuration + platformTransitionHold);
		await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Spiral, platformTransitionDuration, platformTransitionHold);
	}

	private void RestoreSelectedPlatform()
	{
		if (Count == 0)
			return;

		var tree = GetTree();
		if (!tree.HasMeta(SelectedPlatformMetaKey))
			return;

		var platformId = tree.GetMeta(SelectedPlatformMetaKey).AsString();
		if (string.IsNullOrWhiteSpace(platformId))
			return;
		var shouldPlayGbaReturnEject = tree.HasMeta(GbaReturnEjectMetaKey) &&
			tree.GetMeta(GbaReturnEjectMetaKey).AsBool();
		if (tree.HasMeta(GbaReturnEjectMetaKey))
			tree.RemoveMeta(GbaReturnEjectMetaKey);

		for (var i = 0; i < _platforms.Count; i++)
		{
			if (!string.Equals(_platforms[i].Id, platformId, StringComparison.OrdinalIgnoreCase))
				continue;

			_carousel.CarouselPos = i;
			_rememberedPlatformId = _platforms[i].Id;
			if (shouldPlayGbaReturnEject &&
				string.Equals(_platforms[i].Id, "gba", StringComparison.OrdinalIgnoreCase))
			{
				_carousel.PlaySelectedConsoleReturnAnimation(ConsoleCarousel3DView.ConsoleType.GBA);
			}
			return;
		}
	}

	private void RememberSelectedPlatform(int idx)
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
		StartCoverArtWarmup(_platforms[idx]);
	}

	private void StartSelectedPlatformCoverArtWarmup()
	{
		if (_platforms.Count == 0)
			return;

		var idx = Mathf.RoundToInt(_carousel.CarouselPos);
		idx = WrapIndex(idx);
		if (idx < 0 || idx >= _platforms.Count)
			return;

		StartCoverArtWarmup(_platforms[idx]);
	}

	private void StartCoverArtWarmup(PlatformConfig platform)
	{
		BackgroundArtCache.Instance?.PrioritizePlatform(platform);
	}
	
	private void OnCarouselSelectionChanged(int index)
	{
		if (index < 0 || index >= _platforms.Count) return;
		_selectedTitle.Text = _platforms[index].Name;
		RememberSelectedPlatform(index);
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
			new[] { _logout, _searchBarButton, _music, _collections, _inbox, _chat, _settings, _friends, _help },
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
			RememberSelectedPlatform(idx);
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
		UiStyle.StyleGhostNav(0.15f, _prev, _next);

		// Primary actions
		UiStyle.StylePrimaryButton(_selectPlatform);
		UiStyle.AddHoverFeedback(_selectPlatform);
		UiStyle.ApplyParallaxShadow(_selectPlatform, offsetY: 5f);

		// Top bar buttons
		UiStyle.StyleTopBarButton(_logout);
		UiStyle.AddHoverFeedback(_logout);
		UiStyle.ApplyParallaxShadow(_logout);

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

		UiStyle.StyleTopBarButton(_collections);
		UiStyle.AddHoverFeedback(_collections);
		UiStyle.ApplyParallaxShadow(_collections);
		
		UiStyle.StyleTopBarButton(_searchBarButton);
		UiStyle.AddHoverFeedback(_searchBarButton);
		UiStyle.ApplyParallaxShadow(_searchBarButton);
		
		UiStyle.StyleTopBarButton(_filter);
		UiStyle.AddHoverFeedback(_filter);
		UiStyle.ApplyParallaxShadow(_filter);
		
		UiStyle.StyleTopBarButton(_music);
		UiStyle.AddHoverFeedback(_music);
		UiStyle.ApplyParallaxShadow(_music);

		// Labels
		UiStyle.StyleTitleLabel(_selectedTitle);
		UiStyle.StyleStatusLabel(_status);
		
		UiStyle.StyleTextEdit(_searchBarText);
		UiStyle.ApplyParallaxShadow(_searchBarText);
		
	}
}
