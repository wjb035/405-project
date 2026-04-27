using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using PGEmu.app;
using PGEmu.Helpers;
using PGEmu.Services;

public partial class Collections : Control
{
	private const string ReturnSceneMetaKey = "pgemu_return_scene";
	private const string CollectionsFocusMetaKey = "pgemu_collections_focus_name";

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

	// Card prefab spawned into the carousel.
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
	
	// Friend Inbox popup
	[Export] public FriendInbox FriendInboxPopup;

	private readonly List<Control> _cards = new();
	private readonly List<PlatformConfig> _platforms = new();

	// Loaded from `config.json`
	private AppConfig? _config;
	private string? _configPath;

	// Carousel state. `_carouselPos` is continuous so it feels smooth when dragging
	private float _carouselPos = 0f;
	private float _dragStartPos;
	private float _dragStartCarouselPos;
	private bool _dragging;

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

	private Tween _tween;

	public override async void _Ready()
	{
		
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

		_back = GetNodeOrNull<Button>("Margin/Root/Foreground1/TopBar/BtnBack");
		_friends = GetNodeOrNull<Button>(FriendsPath);
		_chat = GetNodeOrNull<Button>(ChatPath);
		_settings = GetNodeOrNull<Button>(SettingsPath);
		_help = GetNodeOrNull<Button>(HelpPath);
		_gameSelect = GetNodeOrNull<Button>(GamePath);
		
		ApplyAesthetic();

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
		LayoutCards();
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

	foreach (var c in _cards)
	{
		c.Visible = false;
		c.MouseFilter = Control.MouseFilterEnum.Ignore;
	}

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
		foreach (var c in _cards)
		{
			c.Visible = true;
			c.MouseFilter = Control.MouseFilterEnum.Stop;
	}
		_lineEdit.Clear();
		_selectPlatform.Show();
		ResetUiNavigationState();
		SpawnCards();
		
	}

	private async void OnBackPressed()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = tree.HasMeta(ReturnSceneMetaKey)
			? tree.GetMeta(ReturnSceneMetaKey).AsString()
			: "res://HomeScreen.tscn";

		if (string.IsNullOrWhiteSpace(returnScene))
			returnScene = "res://HomeScreen.tscn";

		tree.SetMeta(ReturnSceneMetaKey, returnScene);
		await Transition.ChangeScene(returnScene, ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
	}

	private async void OnSettingsPressed()
	{
		AudioManager.Instance?.PlayNavigation(1);
		// Jump to the shared settings screen and return here afterward.
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://Collections.tscn");
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
	
	private void OnInboxPressed()
	{
		FriendInboxPopup.ShowPopup();
	}
	
	private async void OnAchPressed(){
		AudioManager.Instance?.PlayNavigation(1);
		
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		
		await Transition.ChangeScene("res://Achievements.tscn", ScreenTransition.TransitionType.Radial, 0.5f, 0f, true);
		
	}

	private async void OnGamePressed(){
		AudioManager.Instance?.PlayNavigation(1);
		
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://Collections.tscn");
		await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Noise, 0.5f, 0f, false);
	
	}
	
	private void OnChatPressed()
	{
		var overlay = GetNode<ChatOverlay>("/root/ChatOverlay");
		overlay.ToggleOverlay();
	}

	private void OnHelpPressed()
	{
		GD.Print("Help pressed");
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
		tree.SetMeta("pgemu_selected_platform_id", platform.Id);
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);

		await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Noise, 0.5f, 0f, true);
	}

	private void SpawnCards()
	{
		// Clear old cards (e.g. after a reload).
		foreach (var c in _cards)
			c.QueueFree();
		_cards.Clear();
		_platforms.Clear();
			
			
		// THIS IS VERY VERY IFFY RIGHT NOW. FIX THIS LATER!
		if (CollectionStorage.collections.Count == 0)
		{
			//_platforms.AddRange(platforms);
			_platforms.Add(new PlatformConfig { Id = "No collections", Name = "No Collections Yet!" });
			_selectPlatform.Visible = false;
			var card = (Control)CardScene.Instantiate();
				_cardsRoot.AddChild(card);
				_cards.Add(card);

				// `platform_card.tscn` includes a `Panel/Name` label.
				var label = card.GetNodeOrNull<Label>("Panel/Name");
				if (label != null) label.Text = "No collections yet!";
		}
		else
		{
			
			// Keep the carousel usable even when config is missing/empty.
			
			//_platforms.Add(new PlatformConfig { Id = "some collections", Name = "you have some number" });
			foreach (var g in CollectionStorage.collections){
				_platforms.Add(new PlatformConfig { Id = "test", Name = g.Key });
			}
			
			//foreach (var p in CollectionStorage.collections)
			foreach (var p in _platforms)
			{
				var card = (Control)CardScene.Instantiate();
				_cardsRoot.AddChild(card);
				_cards.Add(card);

				// `platform_card.tscn` includes a `Panel/Name` label.
				var label = card.GetNodeOrNull<Label>("Panel/Name");
				if (label != null) label.Text = p.Name;
		}
		}

		//foreach (var p in _platforms)
		
		

		UpdateSelectedLabel();
		UpdateNavEnabled();
	}

	private int Count => _cards.Count;

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
		SnapTo(_carouselPos + dir, true);
	}

	private void SnapTo(float targetPos, bool overshoot)
	{
		// Programmatic move (buttons/wheel): tween to the target position and snap to the nearest item.
		targetPos = WrapPos(targetPos);

		_tween?.Kill();
		_tween = CreateTween();

		// Cubic out feels like a launcher UI, not a robot
		_tween.SetTrans(Tween.TransitionType.Cubic);
		_tween.SetEase(Tween.EaseType.Out);

		if (overshoot)
		{
			// Tiny overshoot using Back
			_tween.SetTrans(Tween.TransitionType.Back);
			_tween.TweenProperty(this, nameof(_carouselPos), targetPos, 0.25f);
		}
		else
		{
			_tween.TweenProperty(this, nameof(_carouselPos), targetPos, 0.22f);
		}

		_tween.TweenCallback(Callable.From(() =>
		{
			_carouselPos = WrapPos(_carouselPos);
			LayoutCards();
			UpdateSelectedLabel();
		}));
	}

	public override void _Process(double delta)
	{
		// Keep layout in sync while tweening and while `_carouselPos` is updated by dragging.
		LayoutCards();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (ShouldIgnoreUiInput()) return;
		if (Count == 0) return;
		if (Count == 1) return;

		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				// Start drag gesture (cancel any in-flight tween).
				_dragging = true;
				_dragStartPos = mb.Position.X;
				_dragStartCarouselPos = _carouselPos;
				_tween?.Kill();
			}
			else
			{
				if (_dragging)
				{
					_dragging = false;
					// On release, snap to the closest card.
					var nearest = Mathf.Round(_carouselPos);
					SnapTo(nearest, false);
				}
			}
		}

		if (_dragging && e is InputEventMouseMotion mm)
		{
			var dx = mm.Position.X - _dragStartPos;

			// Tune sensitivity. Bigger divisor means slower drag.
			_carouselPos = WrapPos(_dragStartCarouselPos - (dx / 520f));
			UpdateSelectedLabel();
		}

		if (e is InputEventMouseButton wheel && wheel.Pressed)
		{
			if (wheel.ButtonIndex == MouseButton.WheelUp) Step(-1);
			if (wheel.ButtonIndex == MouseButton.WheelDown) Step(1);
		}
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
			new[] { _back, _inbox, _friends, _chat, _settings, _help },
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

	private void LayoutCards()
	{
		if (Count == 0) return;

		// Cards are laid out around the container center.
		// The centered card (d ~= 0) is full size/alpha; others scale down and fade out.
		var center = _cardsRoot.Size * 0.5f;
		var spacing = 520f;

		// Render a window around the center, but keep all nodes alive
		for (int i = 0; i < Count; i++)
		{
			var card = _cards[i];

			// Distance from current position, wrapped to [-Count/2, Count/2].
			var d = i - _carouselPos;
			if (d > Count * 0.5f) d -= Count;
			if (d < -Count * 0.5f) d += Count;

			var t = Mathf.Clamp(Mathf.Abs(d), 0f, 1.2f);

			var scale = Mathf.Lerp(1.0f, 0.78f, t);
			var alpha = Mathf.Lerp(1.0f, 0.35f, t);

			var x = center.X + d * spacing;
			var y = center.Y + t * 40f;

			card.PivotOffset = card.Size * 0.5f;
			card.Position = new Vector2(x, y) - card.PivotOffset;

			card.Scale = new Vector2(scale, scale);
			card.Modulate = new Color(1, 1, 1, alpha);

			// Z order so center is on top
			card.ZIndex = (int)(1000 - Mathf.Abs(d) * 100);
		}
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
		UiStyle.StyleGhostNav(_prev, _next);

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
