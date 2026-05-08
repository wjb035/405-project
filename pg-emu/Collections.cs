using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using PGEmu.app;
using PGEmu.Helpers;
using PGEmu.Services;
using PGEmu.UI;

public partial class Collections : Control
{
	private const string ReturnSceneMetaKey = "pgemu_return_scene";
	private const string CollectionsParentReturnSceneMetaKey = "pgemu_collections_parent_return_scene";
	private const string CollectionsFocusMetaKey = "pgemu_collections_focus_name";
	private const string CollectionsScenePath = "res://Collections.tscn";
	private const string HomeScenePath = "res://HomeScreen.tscn";

	// Scene wiring (assigned in `HomeScreen.tscn`).
	[Export] public NodePath CardsPath;
	[Export] public NodePath PrevPath;
	[Export] public NodePath NextPath;
	[Export] public NodePath SelectedTitlePath;
	[Export] public NodePath StatusPath;
	[Export] public NodePath SelectPlatformPath;
	[Export] public NodePath BackPath;
	[Export] public NodePath FriendsPath;
	[Export] public NodePath ChatPath;
	[Export] public NodePath SettingsPath;
	[Export] public NodePath HelpPath;
	[Export] public NodePath GamePath;

	// Legacy 2D card prefab kept on the scene for compatibility with saved node exports.
	[Export] public PackedScene CardScene;
	
	private ScreenTransition Transition =>
		GetNode<ScreenTransition>("/root/ScreenTransition");


	private Control _cardsRoot;
	private Button _prev;
	private Button _next;
	private Label _selectedTitle;
	private Label _status;
	private Button _selectPlatform;
	private Button _back;
	private Button _friends;
	private Button _inbox = null!;
	private Button _chat;
	private Button _settings;
	private Button _help;
	private Button _collectionPrompt;
	private LineEdit _lineEdit;
	private Button _gameSelect;
	private HelpPopup _helpPopup = null!;
	private CollectionCarousel3DView _carousel3D = null!;
	
	// Friend Inbox popup
	[Export] public FriendInbox FriendInboxPopup;

	private readonly List<PlatformConfig> _platforms = new();

	// Loaded from `config.json`
	private AppConfig? _config;
	private string? _configPath;

	// Carousel state mirrored from the 3D collection carousel.
	private float _carouselPos = 0f;

	// Gamepad navigation (left stick + d-pad)
	private const float AxisDeadzone = 0.55f;
	private const int AxisRepeatMs = 180;
	private int _leftAxisDir;
	private long _leftAxisNextMs;
	private int _uiHorizontalAxisDir;
	private long _uiHorizontalAxisNextMs;
	private int _uiVerticalAxisDir;
	private long _uiVerticalAxisNextMs;
	private int _uiRowIndex = -1;
	private int _uiColumnIndex = -1;

	public override async void _Ready()
	{
		CaptureCollectionsParentReturnContext();
		
		// Resolve all node references up front; if a NodePath is wrong you'll fail here with a clear error.
		_cardsRoot = GetNode<Control>("Margin/Root/CenterArea/Mid1/CarouselArea/Cards");
		_prev = GetNode<Button>("Margin/Root/CenterArea/Foreground2/BtnPrev");
		_next = GetNode<Button>("Margin/Root/CenterArea/Foreground2/BtnNext");
		_selectedTitle = GetNode<Label>("Margin/Root/CenterArea/Mid2/SelectedTitle");
		_status = GetNode<Label>("Margin/Root/CenterArea/Mid2/Status");
		_selectPlatform = GetNode<Button>("Margin/Root/CenterArea/Mid2/BottomRow/BtnSelect");
		_inbox = GetNode<Button>("Margin/Root/Foreground1/TopBar/TopIcons/BtnInbox");
		_collectionPrompt = GetNode<Button>("Margin/Root/CenterArea/Mid1/FriendsRow/CollectionPrompt");
		_lineEdit = GetNode<LineEdit>("Margin/Root/CenterArea/Mid1/CarouselArea/LineEdit");
		_lineEdit.Visible = false;
		SetupCollectionCarousel();

		_back = GetNodeOrNull<Button>("Margin/Root/Foreground1/TopBar/BtnBack");
		_friends = GetNodeOrNull<Button>(FriendsPath);
		_chat = GetNodeOrNull<Button>(ChatPath);
		_settings = GetNodeOrNull<Button>(SettingsPath);
		_help = GetNodeOrNull<Button>(HelpPath);
		_gameSelect = GetNodeOrNull<Button>(GamePath);
		
		ApplyAesthetic();
		SetupHelpPopup();

		_prev.Pressed += () => Step(-1);
		_next.Pressed += () => Step(1);
		_selectPlatform.Pressed += OpenSelectedPlatform;

		if (_back != null) _back.Pressed += OnBackPressed;
		if (_settings != null) _settings.Pressed += OnSettingsPressed;
		if (_friends != null) _friends.Pressed += OnFriendsPressed;
		if (_chat != null) _chat.Pressed += OnChatPressed;
		if (_help != null) _help.Pressed += OnHelpPressed;
		if (_gameSelect != null) _gameSelect.Pressed += OnGamePressed;
		_inbox.Pressed += OnInboxPressed;
		var chatManager = GetNode<ChatManager>("/root/ChatManager");
		FriendInboxPopup.AttachBadgeButton(_inbox);
		chatManager.LoadUnreadCounts();
		

		// background transition
		StartBackgroundTransition();
		
		// reset the value if we were in a collection before
		CollectionStorage.currentCollection = null;
		ConnectAllButtons(this);
		InputRoutingService.Instance?.UnlockUiInput();
		ResetUiNavigationState();
		// Load platforms from config, then build the carousel visuals.
		await CollectionStorage.LoadFromJson();
		LoadConfigAndPlatforms();
		SpawnCards();
		ApplyRequestedCollectionFocus();
		_carousel3D.SetCarouselPos(_carouselPos);
		UpdateSelectedLabel();
	}

	private void SetupCollectionCarousel()
	{
		_carousel3D = new CollectionCarousel3DView { Name = "CollectionCarousel3D" };
		_carousel3D.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_carousel3D.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_carousel3D.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_carousel3D.SelectionChanged += OnCollectionCarouselSelectionChanged;
		_cardsRoot.AddChild(_carousel3D);
	}

	private void OnCollectionCarouselSelectionChanged(int index)
	{
		if (Count == 0)
			return;

		_carouselPos = WrapPos(index);
		UpdateSelectedLabel();
	}



private void ConnectAllButtons(Node node)
{
	foreach (Node child in node.GetChildren())
	{
		if (child is Button button &&
			button != _prev &&
			button != _next &&
			button != _selectPlatform &&
			button != _back &&
			button != _settings &&
			button != _friends)
		{
			button.Pressed += () =>
			{
				AudioManager.Instance?.PlayClick();
			};
		}

		ConnectAllButtons(child);
	}
}

private void OnAnyButtonPressed()
{
	var audio = GetNode<AudioManager>("/root/AudioManager");
	audio.PlayClick();
}
	private void OnCollectionPressed()
	{
		ExitUiNavigation();
		_lineEdit.Visible = true;

	_cardsRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
	_carousel3D.Visible = false;
	_carousel3D.MouseFilter = Control.MouseFilterEnum.Ignore;

	_lineEdit.GrabFocus(); // important
}

	private void MakeNewCollection(String text){
		//GD.Print(text);
		CollectionStorage.collections.Add(new KeyValuePair<string, List<GameEntry>>(text, new List<GameEntry>()));
		
		GD.Print("Current Collections List:");
		foreach (var e in CollectionStorage.collections){
			GD.Print(e.Key);
		}
		_lineEdit.Visible = false;

		_cardsRoot.MouseFilter = Control.MouseFilterEnum.Stop;
		_carousel3D.Visible = true;
		_carousel3D.MouseFilter = Control.MouseFilterEnum.Pass;
		_lineEdit.Clear();
		_selectPlatform.Show();
		ResetUiNavigationState();
		SpawnCards();
		
	}

	private async void OnBackPressed()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = ResolveCollectionsBackScene();

		tree.SetMeta(ReturnSceneMetaKey, returnScene);
		await Transition.ChangeScene(returnScene, ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
	}

	private async void OnSettingsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		// Jump to the shared settings screen and return here afterward.
		SetCollectionsReturnContext();
		GetTree().SetMeta("pgemu_settings_tab", "appearance");
		await Transition.ChangeScene("res://Settings.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);

	}

	private async void OnFriendsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		SetCollectionsReturnContext();
		await Transition.ChangeScene("res://profile.tscn", ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
	}
	
	private void OnInboxPressed()
	{
		FriendInboxPopup.ShowPopup();
	}
	
	private async void OnAchPressed(){
		AudioManager.Instance?.PlayNavigation(1);
		SetCollectionsReturnContext();
		await Transition.ChangeScene("res://Achievements.tscn", ScreenTransition.TransitionType.Radial, 0.5f, 0f, true);
		
	}

	private async void OnGamePressed(){
		AudioManager.Instance?.PlayNavigation(1);
		SetCollectionsReturnContext();
		await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Noise, 0.5f, 0f, false);
	
	}

	private void SetCollectionsReturnContext()
	{
		var tree = GetTree();
		PreserveCollectionsParentReturnContext(tree);
		tree.SetMeta(ReturnSceneMetaKey, CollectionsScenePath);
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);
	}

	private void CaptureCollectionsParentReturnContext()
	{
		PreserveCollectionsParentReturnContext(GetTree());
	}

	private void PreserveCollectionsParentReturnContext(SceneTree tree)
	{
		var returnScene = tree.HasMeta(ReturnSceneMetaKey)
			? tree.GetMeta(ReturnSceneMetaKey).AsString()
			: null;

		if (!string.IsNullOrWhiteSpace(returnScene) && !IsCollectionsScene(returnScene))
		{
			tree.SetMeta(CollectionsParentReturnSceneMetaKey, returnScene);
			return;
		}

		if (!tree.HasMeta(CollectionsParentReturnSceneMetaKey))
			tree.SetMeta(CollectionsParentReturnSceneMetaKey, HomeScenePath);
	}

	private string ResolveCollectionsBackScene()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta(ReturnSceneMetaKey)
			? tree.GetMeta(ReturnSceneMetaKey).AsString()
			: null;

		if (!string.IsNullOrWhiteSpace(returnScene) && !IsCollectionsScene(returnScene))
			return returnScene;

		var parentReturnScene = tree.HasMeta(CollectionsParentReturnSceneMetaKey)
			? tree.GetMeta(CollectionsParentReturnSceneMetaKey).AsString()
			: null;

		if (!string.IsNullOrWhiteSpace(parentReturnScene) && !IsCollectionsScene(parentReturnScene))
			return parentReturnScene;

		return HomeScenePath;
	}

	private static bool IsCollectionsScene(string scenePath)
	{
		return string.Equals(scenePath, CollectionsScenePath, StringComparison.OrdinalIgnoreCase);
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
		GD.Print("selection pressed!");
		if (Count == 0) return;

		var idx = Mathf.RoundToInt(_carouselPos);
		idx = WrapIndex(idx);

		if (idx < 0 || idx >= _platforms.Count) return;
		var platform = _platforms[idx];
		AudioManager.Instance?.PlaySelect();
		GD.Print(platform.Name);
		
		// we have to navigate to the games screen, but we have to make sure we're using the right list
		foreach (var c in CollectionStorage.collections){
			if (platform.Name == c.Key){
				CollectionStorage.currentCollection = c.Value;
				break;
			}
		}
		
		
		// Pass selection to the next screen without needing a singleton.
		var tree = GetTree();
		SetCollectionsReturnContext();
		tree.SetMeta("pgemu_selected_platform_id", platform.Id);

		await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Noise, 0.5f, 0f, true);
	}

	private void SpawnCards()
	{
		_platforms.Clear();
			
			
		// THIS IS VERY VERY IFFY RIGHT NOW. FIX THIS LATER!
		if (CollectionStorage.collections.Count == 0)
		{
			//_platforms.AddRange(platforms);
			_platforms.Add(new PlatformConfig { Id = "No collections", Name = "No Collections Yet!" });
			_selectPlatform.Visible = false;
		}
		else
		{
			
			// Keep the carousel usable even when config is missing/empty.
			
			//_platforms.Add(new PlatformConfig { Id = "some collections", Name = "you have some number" });
			foreach (var g in CollectionStorage.collections){
				_platforms.Add(new PlatformConfig { Id = "test", Name = g.Key });
			}
			_selectPlatform.Visible = true;
		}
		var collectionNames = new List<string>();
		foreach (var platform in _platforms)
			collectionNames.Add(platform.Name);

		_carousel3D.Visible = true;
		_carousel3D.MouseFilter = Control.MouseFilterEnum.Pass;
		_carousel3D.Populate(collectionNames, _carouselPos);
		UpdateSelectedLabel();
		UpdateNavEnabled();
	}

	private int Count => _platforms.Count;

	private void ApplyRequestedCollectionFocus()
	{
		var tree = GetTree();
		if (!tree.HasMeta(CollectionsFocusMetaKey))
			return;

		var requestedCollection = tree.GetMeta(CollectionsFocusMetaKey).AsString();
		tree.RemoveMeta(CollectionsFocusMetaKey);

		if (string.IsNullOrWhiteSpace(requestedCollection) || _platforms.Count == 0)
			return;

		for (int index = 0; index < _platforms.Count; index++)
		{
			var platformName = _platforms[index].Name?.Trim();
			if (!string.Equals(platformName, requestedCollection.Trim(), StringComparison.OrdinalIgnoreCase))
				continue;

			_carouselPos = index;
			break;
		}
	}

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
		if (Count <= 1) return;
		AudioManager.Instance?.PlayNavigation(dir);
		_carousel3D.StepDirection(dir);
	}

	public override void _Process(double delta)
	{
		if (_carousel3D != null && Count > 0)
			_carouselPos = _carousel3D.CarouselPos;
		UpdateSelectedLabel();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (ShouldIgnoreUiInput())
			AcceptEvent();
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
				GetViewport().SetInputAsHandled();
				return;
			}

			if (!IsUiNavigationActive() && Count > 1 && HandleAxisNav(jm))
			{
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (e is not InputEventJoypadButton jb || !jb.Pressed)
			return;

		if (!ShouldHandleControllerInput(jb.Device))
			return;

		if (HandleControllerUiButton(jb.ButtonIndex))
		{
			GetViewport().SetInputAsHandled();
			return;
		}

		switch (jb.ButtonIndex)
		{
			case JoyButton.LeftShoulder:
			case JoyButton.DpadLeft:
				if (Count > 1)
				{
					Step(-1);
					GetViewport().SetInputAsHandled();
				}
				break;
			case JoyButton.RightShoulder:
			case JoyButton.DpadRight:
				if (Count > 1)
				{
					Step(1);
					GetViewport().SetInputAsHandled();
				}
				break;
			case JoyButton.A:
				OpenSelectedPlatform();
				GetViewport().SetInputAsHandled();
				break;
			case JoyButton.B:
				if (_back != null)
				{
					OnBackPressed();
					GetViewport().SetInputAsHandled();
				}
				break;
			case JoyButton.Start:
				if (_settings != null)
				{
					OnSettingsPressed();
					GetViewport().SetInputAsHandled();
				}
				break;
			case JoyButton.Touchpad:
				if (_friends != null)
				{
					OnFriendsPressed();
					GetViewport().SetInputAsHandled();
				}
				break;
			case JoyButton.Guide:
				GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
				GetViewport().SetInputAsHandled();
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
					_uiRowIndex = Mathf.Min(1, rows.Count - 1);
					_uiColumnIndex = 0;
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
					_uiRowIndex = dir < 0 ? Mathf.Min(1, rows.Count - 1) : rows.Count - 1;
					_uiColumnIndex = dir < 0 ? 0 : Mathf.Min(1, rows[_uiRowIndex].Count - 1);
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
			new[] { _back, _inbox, _chat, _settings, _friends, _help },
			new[] { _collectionPrompt },
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
		var idx = Mathf.RoundToInt(_carouselPos);
		idx = WrapIndex(idx);

		if (idx >= 0 && idx < _platforms.Count)
		{
			_selectedTitle.Text = _platforms[idx].Name;
			if (_status != null)
				_status.Text = _configPath != null
					? $"{_platforms[idx].Name} selected (loaded {_configPath})"
					: $"{_platforms[idx].Name} selected";
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
			// 1) Shared heuristic (also used by the Avalonia app).
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

			SetStatus($"Loaded config: {_configPath}");
		}
		catch (Exception ex)
		{
			_config = null;
			_configPath = null;
			SetStatus($"Config load failed: {ex.Message}");
		}
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
			bg.StartTransition("GameScreen", 1.5f);
			GD.Print("Background transition finished!");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Gradient transition failed: {ex.Message}");
		}
		
	}
	
	private void ApplyAesthetic()
	{
		// Collections uses the same button/label treatment as Home and GameSelect for consistency.
		// Nav buttons
		UiStyle.StyleNavButton(_prev);
		UiStyle.StyleNavButton(_next);
		UiStyle.StyleGhostNav(0.15f,_prev, _next);

		// Primary actions
		UiStyle.StylePrimaryButton(_selectPlatform);
		UiStyle.AddHoverFeedback(_selectPlatform);
		UiStyle.ApplyParallaxShadow(_selectPlatform);
		
		UiStyle.StylePrimaryButton(_collectionPrompt);
		UiStyle.AddHoverFeedback(_collectionPrompt);
		UiStyle.ApplyParallaxShadow(_collectionPrompt);

		// Top bar buttons
		UiStyle.StyleTopBarButton(_back);
		UiStyle.AddHoverFeedback(_back);
		UiStyle.ApplyParallaxShadow(_back);

		UiStyle.StyleTopBarButton(_friends);
		UiStyle.AddHoverFeedback(_friends);
		UiStyle.ApplyParallaxShadow(_friends);

		UiStyle.StyleTopBarButton(_chat);
		UiStyle.AddHoverFeedback(_chat);
		UiStyle.ApplyParallaxShadow(_chat);

		UiStyle.StyleTopBarButton(_settings);
		UiStyle.AddHoverFeedback(_settings);
		UiStyle.ApplyParallaxShadow(_settings);

		UiStyle.StyleTopBarButton(_inbox);
		UiStyle.AddHoverFeedback(_inbox);
		UiStyle.ApplyParallaxShadow(_inbox);

		UiStyle.StyleTopBarButton(_help);
		UiStyle.AddHoverFeedback(_help);
		UiStyle.ApplyParallaxShadow(_help);

		UiStyle.StyleTopBarButton(_gameSelect);
		UiStyle.AddHoverFeedback(_gameSelect);
		UiStyle.ApplyParallaxShadow(_gameSelect);
		
		// Labels
		UiStyle.StyleTitleLabel(_selectedTitle);
		UiStyle.StyleStatusLabel(_status);

		// Input
		UiStyle.StyleLineEdit(_lineEdit);
	}
}
