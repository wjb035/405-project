using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PGEmu.app;
using System.Threading.Tasks;
using RetroAchievements.Api;
using PGEmu.Services;


public partial class GameSelect : Control
{
	// NodePaths assigned in GameSelect.tscn so we can wire UI in-editor without hardcoding paths.
	[Export] public NodePath CardsPath;
	[Export] public NodePath PrevPath;
	[Export] public NodePath NextPath;
	[Export] public NodePath TitlePath;
	[Export] public NodePath MetaLeftPath;
	[Export] public NodePath MetaRightPath;
	[Export] public NodePath StatusPath;
	[Export] public NodePath BackPath;
	[Export] public NodePath PlayPath;
	[Export] public NodePath SettingsPath;

	// Prefab for a single carousel card.
	[Export] public PackedScene CardScene;

	// Cached scene nodes, resolved in _Ready().
	private Control _cardsRoot = null!;
	private Button _prev = null!;
	private Button _next = null!;
	private Label _title = null!;
	private Label _metaLeft = null!;
	private Label _metaRight = null!;
	private Label _status = null!;
	private Button _back = null!;
	private Button _play = null!;
	private Button _settings = null!;
	OptionButton _optionButton = new OptionButton();
	
	private Button _achievement = null!;

	// UI instances and data backing the carousel.
	private readonly List<Control> _cards = new();
	private readonly List<GameEntry> _games = new();

	
	// Loaded app context.
	private AppConfig? _config;
	private string? _configPath;
	private PlatformConfig? _platform;

	// Carousel state.
	// _carouselPos is continuous so dragging and tweens feel smooth (ex: 2.35 between cards).
	private float _carouselPos = 0f;
	private float _dragStartPos;          // Mouse-down X position.
	private float _dragStartCarouselPos;  // Carousel position at mouse-down.
	private bool _dragging;

	// Gamepad navigation (left stick + d-pad)
	private const float AxisDeadzone = 0.55f;
	private const int AxisRepeatMs = 180;
	private int _leftAxisDir;
	private long _leftAxisNextMs;

	// Active snap tween, killed on new input to keep things responsive.
	private Tween? _tween;

	public override async void _Ready()
	{
		
		// Resolve exported node paths into actual nodes.
		_cardsRoot = GetNode<Control>(CardsPath);
		_prev = GetNode<Button>(PrevPath);
		_next = GetNode<Button>(NextPath);
		_title = GetNode<Label>(TitlePath);
		_metaLeft = GetNode<Label>(MetaLeftPath);
		_metaRight = GetNode<Label>(MetaRightPath);
		_status = GetNode<Label>(StatusPath);
		_back = GetNode<Button>(BackPath);
		_play = GetNode<Button>(PlayPath);
		_settings = GetNode<Button>(SettingsPath);
		_achievement = GetNode<Button>("Margin/Root/TopBar/TopIcons/BtnAch");
			
			
		
		_optionButton.Name = "test";
		var container = GetNode<HBoxContainer>("Margin/Root/CenterArea/CarouselArea/HBoxContainer");
   		container.AddChild(_optionButton);
		//_optionButton.AddItem("Option A", 0);
		_optionButton.Hide();
		int i = 0;
		foreach (var c in CollectionStorage.collections){
			_optionButton.AddItem(c.Key, i);
			i++;
		}
		 _optionButton.ItemSelected += SelectedOption;
		UiStyle.StyleOptionButton(_optionButton);
		
		// Button events.
		_prev.Pressed += () => Step(-1);
		_next.Pressed += () => Step(1);
		_back.Pressed += GoBack;
		_play.Pressed += PlaySelected;
		_settings.Pressed += OpenVault;
		ApplyAesthetic();
		
		ConnectAllButtons(this);
		GD.Print("IN GAME SELECT");
		InputRoutingService.Instance?.UnlockUiInput();
		// Load data and build UI.
		await LoadContextAndGames();
		SpawnCards();
		LayoutCards();
		UpdateSelectionUI();
		_achievement.Show();
		
		
	}

private void ConnectAllButtons(Node node)
{
	foreach (Node child in node.GetChildren())
	{
		if (child is Button button)
		{
			// Correct way to connect in Godot 4 C#
			button.Pressed += () =>
			{
				AudioManager.Instance?.PlaySfx("res://audio/click.wav");
			};
		}

		// Recurse into children
		ConnectAllButtons(child);
	}
}

private void OnAnyButtonPressed()
{
	var audio = GetNode<AudioManager>("/root/AudioManager");
	audio.PlaySfx("res://audio/click.wav");
}


	private void GoBack()
	{
		CollectionStorage.currentCollection = null;
		// Navigate back to the home screen scene.
		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
	}
	private void addToCollection(){
		GD.Print("button has been pressed!!!!!!");
		if (_optionButton.Visible){
			_optionButton.Hide();
		}else if (CollectionStorage.collections.Count > 0){
			
			_optionButton.Show();
			_optionButton.Select(-1);
		}
		
		
	}
	
	private void SelectedOption(long index){
		 GD.Print("User selected: " + _optionButton.GetItemText((int)index));
		foreach (var c in CollectionStorage.collections){
			if (c.Key == _optionButton.GetItemText((int)index)){
				//GD.Print("found it!");
				GetSelectedGame().platform = _platform;
				c.Value.Add(GetSelectedGame());
				foreach (var g in c.Value){
					GD.Print(g.Name);
				}
				
				CollectionStorage.saveToJson();
			}
		}
		GD.Print(GetSelectedGame().Name);
		
		
		_optionButton.Hide();
		
	}
	private void OpenVault()
	{
		// Store return context in SceneTree meta so Vault can return here with the same config.
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://GameSelect.tscn");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);

		tree.ChangeSceneToFile("res://vault.tscn");
	}

	private void OpenProfile()
	{
		// Store return context so Profile can route back to this scene.
		CollectionStorage.currentCollection = null;
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://GameSelect.tscn");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);

		tree.ChangeSceneToFile("res://profile.tscn");
	}

	private void GoHome()
	{
		CollectionStorage.currentCollection = null;
		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
	}
	
	private void GoAch()
	{
		AchievementStorage.gameName = GetSelectedGame().Name;
		AchievementStorage.gameId = GetSelectedGame().retroAchievementsGameId;
		GetTree().ChangeSceneToFile("res://Achievements.tscn");
	}

	private void PlaySelected()
	{
		if (ShouldIgnoreUiInput())
			return;

		GD.Print(GetSelectedGame().Name);
		if (CollectionStorage.currentCollection == null){
		// Can't launch without a loaded config + platform context.
		if (_config == null || _platform == null)
		{
			SetStatus("Can't launch: config or platform missing.");
			return;
		}

		// Selected game is based on the current carousel center position.
		var game = GetSelectedGame();
		if (game == null)
		{
			SetStatus("No game selected.");
			return;
		}
		
		try
		{
			// Delegate launching to app layer.
			if (CollectionStorage.currentCollection == null){
			Launcher.LaunchFromConfig(_config, _platform, game);
			InputRoutingService.Instance?.LockUiInputForExternalLaunch();
			SetStatus($"Launching: {game.Title}");
			}
			else{
				Launcher.LaunchFromConfig(_config, game.platform, game);
			InputRoutingService.Instance?.LockUiInputForExternalLaunch();
			SetStatus($"Launching: {game.Title}");
			}
		}
		catch (Exception ex)
		{
			InputRoutingService.Instance?.UnlockUiInput();
			// Surface launch errors to UI instead of crashing.
			SetStatus($"Launch failed: {ex.Message}");
		}
		}
		
		else{
			
			var game = GetSelectedGame();
			if (game == null)
			{
				SetStatus("No game selected.");
				return;
			}
			try
				{
					// Delegate launching to app layer.
					
					Launcher.LaunchFromConfig(_config, game.platform, game);
					InputRoutingService.Instance?.LockUiInputForExternalLaunch();
					SetStatus($"Launching: {game.Title}");
					
				}
				catch (Exception ex)
				{
					InputRoutingService.Instance?.UnlockUiInput();
					
					GD.Print(game.platform.Name);
					GD.Print(ex);
					// Surface launch errors to UI instead of crashing.
					SetStatus($"Launch failed: {ex.Message}");
				}
		}
	}

	private async Task LoadContextAndGames()
	{
		GD.Print("in load context");
		try
		{
			if (CollectionStorage.currentCollection == null){
				GD.Print("IN TRY CATCH");
				if (CollectionStorage.currentCollection !=null){
				foreach (var g in CollectionStorage.currentCollection){
					GD.Print(g.Name);
				}
				}
			var tree = GetTree();

			// Prefer config path passed from a previous scene, fall back to heuristics.
			_configPath = tree.HasMeta("pgemu_config_path")
				? tree.GetMeta("pgemu_config_path").AsString()
				: null;

			_configPath = string.IsNullOrWhiteSpace(_configPath) ? null : _configPath;
			_configPath ??= ConfigFinder.FindConfigPath();
			_configPath ??= TryFindConfigNearGodotProject();

			if (_configPath == null)
			{
				_config = null;
				_platform = null;
				SetStatus("config.json not found.");
				return;
			}

			// Load config and normalize LibraryRoot so "~" works cross-machine.
			_config = AppConfig.Load(_configPath);
			_config.LibraryRoot = ExpandHomePath(_config.LibraryRoot);

			// Platform selection is passed through SceneTree metadata if available.
			var platformId = tree.HasMeta("pgemu_selected_platform_id")
				? tree.GetMeta("pgemu_selected_platform_id").AsString()
				: null;

			platformId = string.IsNullOrWhiteSpace(platformId) ? null : platformId;

			_platform = platformId != null
				? _config.Platforms.FirstOrDefault(p =>
					string.Equals(p.Id, platformId, StringComparison.OrdinalIgnoreCase))
				: _config.Platforms.FirstOrDefault();
			GD.Print("ABOVE PLATFORM NULL");
			
			// if the platform is null AND we're not coming from collections
			if (_platform == null && CollectionStorage.currentCollection == null)
			{
				SetStatus("No platform selected.");
				GD.Print("ABOVE RETURN");
				return;
			}
			GD.Print("BELOW PLATFORM NULL!");

			// Scan the platform's library directory for compatible ROM files.
			_games.Clear();
			var scanned = LibraryScanner.Scan(_platform, _config.LibraryRoot, out var scanDir);

			// If the expected ROM directory doesn't exist, report the exact resolved path.
			if (!Directory.Exists(scanDir))
			{
				SetStatus(
					$"Library folder not found for {_platform.Name}. " +
					$"Dir='{scanDir}'. (LibraryRoot='{_config.LibraryRoot}', RomPath='{_platform.RomPath}')");
				return;
			}

			SetStatus($"Scanning: {scanDir}");
			
			// if we have already loaded the games before, load them from the hash table they're stored in.
			// this cuts down the loading time (and is considerably nicer to the RA API, since it only gets the massive list once per platform)
			// but it basically means that if a game is added when you're in a different screen,
			// but have already loaded the games, it won't be detected till the next launch of the program.
			// this can be fixed, but at this point it's a little niche to spend time on such a minor inconvenience-- definitely can be fixed later though
			
			// UNCOMMENT LATER
			if (AchievementStorage.gameToString.ContainsKey(_platform.retroachievementsPlatformID)){
				var prevGames = (IEnumerable<GameEntry>)AchievementStorage.gameToString[_platform.retroachievementsPlatformID];
				
				foreach (var g in prevGames){
					_games.Add(g);
				}
			}
			else{
				foreach (var g in scanned){
					_games.Add(g);
				}
				}
			
			GD.Print("above if else");
			if (CollectionStorage.currentCollection != null){
				_games.Clear();
				//_games = CollectionStorage.currentCollection;
				foreach (var g in CollectionStorage.currentCollection){
					_games.Add(g);
					GD.Print(g.Name);
				}
			}
			else{
				GD.Print("NULL NULL NULL");
			}
				//GameEntry? temp = _games[0];
				//_games.Remove(temp);
				//_games.Add(temp);
				
				
			
			

			// Keep ordering stable and predictable.
			_games.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
			
			
			
			
			
			
			
			
			await RetroAchievementsService.Retro(_platform, _games);
			
			
			
			if (_games.Count == 0)
			{
				SetStatus(
					$"No games found for {_platform.Name}. " +
					$"Dir='{scanDir}', Extensions=[{string.Join(", ", _platform.Extensions)}].");
			}
		}
		else{
			GD.Print("hi in else");
			var tree = GetTree();

			// Prefer config path passed from a previous scene, fall back to heuristics.
			_configPath = tree.HasMeta("pgemu_config_path")
				? tree.GetMeta("pgemu_config_path").AsString()
				: null;

			_configPath = string.IsNullOrWhiteSpace(_configPath) ? null : _configPath;
			_configPath ??= ConfigFinder.FindConfigPath();
			_configPath ??= TryFindConfigNearGodotProject();

			if (_configPath == null)
			{
				_config = null;
				_platform = null;
				SetStatus("config.json not found.");
				return;
			}

			// Load config and normalize LibraryRoot so "~" works cross-machine.
			_config = AppConfig.Load(_configPath);
			_config.LibraryRoot = ExpandHomePath(_config.LibraryRoot);
		
			foreach (var g in CollectionStorage.currentCollection){
					_games.Add(g);
					GD.Print(g.Name + "belongs to the ");
					GD.Print(g.platform.Name	);
				}
		}
		}
		
		
		catch (Exception ex)
		{
			// Reset state so the rest of the screen doesn't operate on half-initialized data.
			_config = null;
			_configPath = null;
			_platform = null;
			_games.Clear();
			SetStatus($"Load failed: {ex.Message}");
		}
	}

	private void SpawnCards()
	{
		// Destroy existing card nodes before rebuilding.
		foreach (var c in _cards)
			c.QueueFree();
		_cards.Clear();

		// Keep the carousel visible even with zero results by injecting a placeholder entry.
		if (_games.Count == 0)
		{
			var placeholder = new GameEntry { Name = "No games found", Path = "" };
			_games.Add(placeholder);
		}

		// Create a card instance for each game and set its label text.
		foreach (var g in _games)
		{
			var card = (Control)CardScene.Instantiate();
			_cardsRoot.AddChild(card);
			_cards.Add(card);

			// Reuse `platform_card.tscn` label node path.
			var label = card.GetNodeOrNull<Label>("Panel/Name");
			if (label != null) label.Text = g.Title;
		}

		UpdateNavEnabled();
	}

	private int Count => _cards.Count;

	private int WrapIndex(int i)
	{
		// Wrap an integer index into [0..Count-1] so the carousel loops.
		if (Count == 0) return 0;
		i %= Count;
		if (i < 0) i += Count;
		return i;
	}

	private float WrapPos(float p)
	{
		// Wrap a continuous position into [0..Count) for smooth looping motion.
		if (Count == 0) return 0f;
		p %= Count;
		if (p < 0) p += Count;
		return p;
	}

	private void Step(int dir)
	{
		// Move one card left/right.
		if (Count <= 1) return;
		SnapTo(_carouselPos + dir, overshoot: true);
	}

	private void SnapTo(float targetPos, bool overshoot)
	{
		// Snap (tween) carousel position to the target, then normalize and refresh UI.
		targetPos = WrapPos(targetPos);

		// Stop any existing tween so multiple clicks/drags don't fight.
		_tween?.Kill();

		_tween = CreateTween();
		_tween.SetTrans(overshoot ? Tween.TransitionType.Back : Tween.TransitionType.Cubic);
		_tween.SetEase(Tween.EaseType.Out);

		// Animate the backing field via property name.
		_tween.TweenProperty(this, nameof(_carouselPos), targetPos, overshoot ? 0.25f : 0.22f);

		// After tween, normalize and update visuals.
		_tween.TweenCallback(Callable.From(() =>
		{
			_carouselPos = WrapPos(_carouselPos);
			LayoutCards();
			UpdateSelectionUI();
		}));
	}

	public override void _Process(double delta)
	{
		// Layout every frame so drag updates look continuous.
		LayoutCards();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (ShouldIgnoreUiInput()) return;
		if (Count <= 1) return;

		// Drag handling.
		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				_dragging = true;
				_dragStartPos = mb.Position.X;
				_dragStartCarouselPos = _carouselPos;

				// Stop snapping while dragging.
				_tween?.Kill();
			}
			else if (_dragging)
			{
				// On release, snap to the nearest whole card index.
				_dragging = false;
				var nearest = Mathf.Round(_carouselPos);
				SnapTo(nearest, overshoot: false);
			}
		}

		// Convert mouse movement into carousel position changes.
		if (_dragging && e is InputEventMouseMotion mm)
		{
			var dx = mm.Position.X - _dragStartPos;

			// 520f matches the spacing used by LayoutCards().
			_carouselPos = WrapPos(_dragStartCarouselPos - (dx / 520f));

			// Update labels/button state during drag so selection feels live.
			UpdateSelectionUI();
		}

		// Wheel scroll steps between cards.
		if (e is InputEventMouseButton wheel && wheel.Pressed)
		{
			if (wheel.ButtonIndex == MouseButton.WheelUp) Step(-1);
			if (wheel.ButtonIndex == MouseButton.WheelDown) Step(1);
		}
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (ShouldIgnoreUiInput()) return;

		if (e is not InputEventJoypadButton jb || !jb.Pressed)
		{
			if (Count > 1 &&
				e is InputEventJoypadMotion jm &&
				ShouldHandleControllerInput(jm.Device) &&
				HandleAxisNav(jm))
			{
				MarkInputHandled();
			}
			return;
		}

		if (!ShouldHandleControllerInput(jb.Device))
			return;

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
			case JoyButton.X:
				MarkInputHandled();
				PlaySelected();
				break;
			case JoyButton.B:
				MarkInputHandled();
				GoBack();
				break;
			case JoyButton.Start:
				MarkInputHandled();
				OpenVault();
				break;
			case JoyButton.Touchpad:
				MarkInputHandled();
				OpenProfile();
				break;
			case JoyButton.Guide:
				MarkInputHandled();
				GoHome();
				break;
		}
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
		var dir = 0;
		if (value <= -AxisDeadzone) dir = -1;
		else if (value >= AxisDeadzone) dir = 1;

		if (dir == 0)
		{
			heldDir = 0;
			return false;
		}

		var now = (long)Time.GetTicksMsec();
		if (dir != heldDir || now >= nextMs)
		{
			Step(dir);
			heldDir = dir;
			nextMs = now + AxisRepeatMs;
			return true;
		}

		return false;
	}

	private void LayoutCards()
	{
		if (Count == 0) return;

		// Center of the cards container.
		var center = _cardsRoot.Size * 0.5f;

		// Horizontal spacing between cards.
		var spacing = 520f;

		for (int i = 0; i < Count; i++)
		{
			var card = _cards[i];

			// Distance from center in "card units".
			var d = i - _carouselPos;

			// Wrap distance so the shortest path is used (looping carousel illusion).
			if (d > Count * 0.5f) d -= Count;
			if (d < -Count * 0.5f) d += Count;

			// t is how far a card is from center (clamped to keep falloff sane).
			var t = Mathf.Clamp(Mathf.Abs(d), 0f, 1.2f);

			// Scale and fade cards as they move away from the center.
			var scale = Mathf.Lerp(1.0f, 0.78f, t);
			var alpha = Mathf.Lerp(1.0f, 0.35f, t);

			// Position cards along X with a slight Y drop for depth.
			var x = center.X + d * spacing;
			var y = center.Y + t * 40f;

			// Pivot at center so scaling doesn't shift the card.
			card.PivotOffset = card.Size * 0.5f;

			// Apply transform and draw-order.
			card.Position = new Vector2(x, y) - card.PivotOffset;
			card.Scale = new Vector2(scale, scale);
			card.Modulate = new Color(1, 1, 1, alpha);

			// Higher ZIndex for cards closer to center so overlap looks correct.
			card.ZIndex = (int)(1000 - Mathf.Abs(d) * 100);
		}
	}

	private GameEntry? GetSelectedGame()
	{
		// Selection is whichever card index is closest to the center position.
		if (_games.Count == 0) return null;
		var idx = WrapIndex(Mathf.RoundToInt(_carouselPos));
		if (idx < 0 || idx >= _games.Count) return null;
		return _games[idx];
	}

	private void UpdateSelectionUI()
	{
		// Update title and metadata based on selected game + platform context.
		var game = GetSelectedGame();
		_title.Text = game?.Title ?? "";

		var platformName = _platform?.Name ?? "Unknown Platform";
		_metaLeft.Text = $"{platformName} • {_games.Count} game(s)";
		_metaRight.Text = game != null ? $"Achievements: {game.AchievementNum}" : "";
		GD.Print(game.AchievementNum);	
		// Disable Play if we cannot launch or if the selected entry has no path.
		_play.Disabled = _config == null || _platform == null || game == null || string.IsNullOrEmpty(game.Path);
		//enable play if we came from collections
		if (CollectionStorage.currentCollection != null){
			_play.Disabled = false;
		}
	}

	private void UpdateNavEnabled()
	{
		// Only allow prev/next when there is something to navigate.
		var enabled = Count > 1;
		_prev.Disabled = !enabled;
		_next.Disabled = !enabled;
	}

	private void SetStatus(string text)
	{
		// Single place to push status text to the UI.
		_status.Text = text;
	}

	private static string? TryFindConfigNearGodotProject()
	{
		// Best-effort config discovery for when you run the scene directly from the editor.
		try
		{
			var projectDir = ProjectSettings.GlobalizePath("res://");

			var inProject = Path.Combine(projectDir, "config.json");
			if (File.Exists(inProject)) return inProject;

			var inParent = Path.GetFullPath(Path.Combine(projectDir, "..", "config.json"));
			if (File.Exists(inParent)) return inParent;
		}
		catch
		{
			// Ignore, this is just a convenience path.
		}

		return null;
	}

	private static string ExpandHomePath(string path)
	{
		// Expand "~" and "~/" to the user's home directory.
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
		// Match game selection controls to the same launcher palette and contrast rules.
		UiStyle.StyleNavButton(_prev);
		UiStyle.StyleNavButton(_next);
		UiStyle.StylePrimaryButton(_play);
		UiStyle.StyleTopBarButton(_back);
		UiStyle.StyleTopBarButton(_settings);
		UiStyle.StyleTopBarButton(_achievement);
		UiStyle.StyleTopBarButton(GetNodeOrNull<Button>("Margin/Root/TopBar/TopIcons/BtnFriends"));
		UiStyle.StyleTopBarButton(GetNodeOrNull<Button>("Margin/Root/TopBar/TopIcons/BtnChat"));
		UiStyle.StyleTopBarButton(GetNodeOrNull<Button>("Margin/Root/TopBar/TopIcons/BtnHelp"));
		UiStyle.StyleTitleLabel(_title);
		UiStyle.StyleMetaLabel(_metaLeft);
		UiStyle.StyleMetaLabel(_metaRight);
		UiStyle.StyleStatusLabel(_status);
	}
}
