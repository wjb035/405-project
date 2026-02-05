using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PGEmu.app;
using RetroAchievements.Api;


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

	public override void _Ready()
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

		// Button events.
		_prev.Pressed += () => Step(-1);
		_next.Pressed += () => Step(1);
		_back.Pressed += GoBack;
		_play.Pressed += PlaySelected;
		_settings.Pressed += OpenVault;

		// Load data and build UI.
		LoadContextAndGames();
		SpawnCards();
		LayoutCards();
		UpdateSelectionUI();
	}

	private void GoBack()
	{
		// Navigate back to the home screen scene.
		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
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
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://GameSelect.tscn");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);

		tree.ChangeSceneToFile("res://profile.tscn");
	}

	private void GoHome()
	{
		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
	}

	private void PlaySelected()
	{
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
			Launcher.LaunchFromConfig(_config, _platform, game);
			SetStatus($"Launching: {game.Title}");
		}
		catch (Exception ex)
		{
			// Surface launch errors to UI instead of crashing.
			SetStatus($"Launch failed: {ex.Message}");
		}
	}

	private void LoadContextAndGames()
	{
		try
		{
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

			if (_platform == null)
			{
				SetStatus("No platform selected.");
				return;
			}

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

			foreach (var g in scanned)
				_games.Add(g);

			// Keep ordering stable and predictable.
			_games.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
			
			
			// Debug stuff, removing later
			GD.Print("Before:");
			foreach (var g in _games){
				//GD.Print(g.Title + g.AchievementNum);
			}
			
			
			
			string username = "badacctname";
			string apiKey = "";
			
			RetroAchievementsHttpClient client = new RetroAchievementsHttpClient(new RetroAchievementsAuthenticationData(username, apiKey));
			RetroAchievementsService.Retro(client, _platform, _games);
			GD.Print("After:");
			foreach (var g in _games){
				//GD.Print(g.Title + g.AchievementNum);
			}
			
			
			if (_games.Count == 0)
			{
				SetStatus(
					$"No games found for {_platform.Name}. " +
					$"Dir='{scanDir}', Extensions=[{string.Join(", ", _platform.Extensions)}].");
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
		if (e is not InputEventJoypadButton jb || !jb.Pressed)
		{
			if (Count > 1 && e is InputEventJoypadMotion jm && HandleAxisNav(jm))
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
			case JoyButton.X:
				PlaySelected();
				GetViewport().SetInputAsHandled();
				break;
			case JoyButton.B:
				GoBack();
				GetViewport().SetInputAsHandled();
				break;
			case JoyButton.Start:
				OpenVault();
				GetViewport().SetInputAsHandled();
				break;
			case JoyButton.Touchpad:
				OpenProfile();
				GetViewport().SetInputAsHandled();
				break;
			case JoyButton.Guide:
				GoHome();
				GetViewport().SetInputAsHandled();
				break;
		}
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

		// Disable Play if we cannot launch or if the selected entry has no path.
		_play.Disabled = _config == null || _platform == null || game == null || string.IsNullOrEmpty(game.Path);
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
}
