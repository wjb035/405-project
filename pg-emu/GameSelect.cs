using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PGEmu.app;

public partial class GameSelect : Control
{
	// Scene wiring (assigned in `GameSelect.tscn`).
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

	// Card prefab spawned into the carousel.
	[Export] public PackedScene CardScene;

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

	private readonly List<Control> _cards = new();
	private readonly List<GameEntry> _games = new();

	private AppConfig? _config;
	private string? _configPath;
	private PlatformConfig? _platform;

	// Carousel state. `_carouselPos` is continuous so drag/tween feels smooth (e.g. 2.35 between cards).
	private float _carouselPos = 0f;
	private float _dragStartPos;
	private float _dragStartCarouselPos;
	private bool _dragging;

	private Tween? _tween;

	public override void _Ready()
	{
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

		_prev.Pressed += () => Step(-1);
		_next.Pressed += () => Step(1);
		_back.Pressed += GoBack;
		_play.Pressed += PlaySelected;
		_settings.Pressed += OpenVault;

		LoadContextAndGames();
		SpawnCards();
		LayoutCards();
		UpdateSelectionUI();
	}

	private void GoBack()
	{
		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
	}

	private void OpenVault()
	{
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://GameSelect.tscn");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);
		tree.ChangeSceneToFile("res://vault.tscn");
	}

	private void PlaySelected()
	{
		if (_config == null || _platform == null)
		{
			SetStatus("Can't launch: config or platform missing.");
			return;
		}

		var game = GetSelectedGame();
		if (game == null)
		{
			SetStatus("No game selected.");
			return;
		}

		try
		{
			Launcher.LaunchFromConfig(_config, _platform, game);
			SetStatus($"Launching: {game.Title}");
		}
		catch (Exception ex)
		{
			SetStatus($"Launch failed: {ex.Message}");
		}
	}

	private void LoadContextAndGames()
	{
		try
		{
			// Prefer the path passed from HomeScreen, but fall back to the shared heuristic.
			var tree = GetTree();
			_configPath = tree.HasMeta("pgemu_config_path") ? tree.GetMeta("pgemu_config_path").AsString() : null;
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

			_config = AppConfig.Load(_configPath);
			_config.LibraryRoot = ExpandHomePath(_config.LibraryRoot);

			var platformId = tree.HasMeta("pgemu_selected_platform_id") ? tree.GetMeta("pgemu_selected_platform_id").AsString() : null;
			platformId = string.IsNullOrWhiteSpace(platformId) ? null : platformId;

			_platform = platformId != null
				? _config.Platforms.FirstOrDefault(p => string.Equals(p.Id, platformId, StringComparison.OrdinalIgnoreCase))
				: _config.Platforms.FirstOrDefault();

			if (_platform == null)
			{
				SetStatus("No platform selected.");
				return;
			}

			_games.Clear();
			var scanned = LibraryScanner.Scan(_platform, _config.LibraryRoot, out var scanDir);
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

			_games.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

			if (_games.Count == 0)
			{
				SetStatus(
					$"No games found for {_platform.Name}. " +
					$"Dir='{scanDir}', Extensions=[{string.Join(", ", _platform.Extensions)}].");
			}
		}
		catch (Exception ex)
		{
			_config = null;
			_configPath = null;
			_platform = null;
			_games.Clear();
			SetStatus($"Load failed: {ex.Message}");
		}
	}

	private void SpawnCards()
	{
		foreach (var c in _cards)
			c.QueueFree();
		_cards.Clear();

		if (_games.Count == 0)
		{
			// Keep the carousel present even when empty.
			var placeholder = new GameEntry { Name = "No games found", Path = "" };
			_games.Add(placeholder);
		}

		foreach (var g in _games)
		{
			var card = (Control)CardScene.Instantiate();
			_cardsRoot.AddChild(card);
			_cards.Add(card);

			// Reuse `platform_card.tscn` label path.
			var label = card.GetNodeOrNull<Label>("Panel/Name");
			if (label != null) label.Text = g.Title;
		}

		UpdateNavEnabled();
	}

	private int Count => _cards.Count;

	private int WrapIndex(int i)
	{
		if (Count == 0) return 0;
		i %= Count;
		if (i < 0) i += Count;
		return i;
	}

	private float WrapPos(float p)
	{
		if (Count == 0) return 0f;
		p %= Count;
		if (p < 0) p += Count;
		return p;
	}

	private void Step(int dir)
	{
		if (Count <= 1) return;
		SnapTo(_carouselPos + dir, overshoot: true);
	}

	private void SnapTo(float targetPos, bool overshoot)
	{
		targetPos = WrapPos(targetPos);

		_tween?.Kill();
		_tween = CreateTween();
		_tween.SetTrans(overshoot ? Tween.TransitionType.Back : Tween.TransitionType.Cubic);
		_tween.SetEase(Tween.EaseType.Out);
		_tween.TweenProperty(this, nameof(_carouselPos), targetPos, overshoot ? 0.25f : 0.22f);
		_tween.TweenCallback(Callable.From(() =>
		{
			_carouselPos = WrapPos(_carouselPos);
			LayoutCards();
			UpdateSelectionUI();
		}));
	}

	public override void _Process(double delta)
	{
		LayoutCards();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (Count <= 1) return;

		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				_dragging = true;
				_dragStartPos = mb.Position.X;
				_dragStartCarouselPos = _carouselPos;
				_tween?.Kill();
			}
			else if (_dragging)
			{
				_dragging = false;
				var nearest = Mathf.Round(_carouselPos);
				SnapTo(nearest, overshoot: false);
			}
		}

		if (_dragging && e is InputEventMouseMotion mm)
		{
			var dx = mm.Position.X - _dragStartPos;
			_carouselPos = WrapPos(_dragStartCarouselPos - (dx / 520f));
			UpdateSelectionUI();
		}

		if (e is InputEventMouseButton wheel && wheel.Pressed)
		{
			if (wheel.ButtonIndex == MouseButton.WheelUp) Step(-1);
			if (wheel.ButtonIndex == MouseButton.WheelDown) Step(1);
		}
	}

	private void LayoutCards()
	{
		if (Count == 0) return;

		var center = _cardsRoot.Size * 0.5f;
		var spacing = 520f;

		for (int i = 0; i < Count; i++)
		{
			var card = _cards[i];

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
			card.ZIndex = (int)(1000 - Mathf.Abs(d) * 100);
		}
	}

	private GameEntry? GetSelectedGame()
	{
		if (_games.Count == 0) return null;
		var idx = WrapIndex(Mathf.RoundToInt(_carouselPos));
		if (idx < 0 || idx >= _games.Count) return null;
		return _games[idx];
	}

	private void UpdateSelectionUI()
	{
		var game = GetSelectedGame();
		_title.Text = game?.Title ?? "";

		// Mockup-style metadata placeholders (can be replaced with real stats/RA later).
		var platformName = _platform?.Name ?? "Unknown Platform";
		_metaLeft.Text = $"{platformName} • {_games.Count} game(s)";
		_metaRight.Text = game != null ? $"Achievements: {game.AchievementNum}" : "";

		_play.Disabled = _config == null || _platform == null || game == null || string.IsNullOrEmpty(game.Path);
	}

	private void UpdateNavEnabled()
	{
		var enabled = Count > 1;
		_prev.Disabled = !enabled;
		_next.Disabled = !enabled;
	}

	private void SetStatus(string text)
	{
		_status.Text = text;
	}

	private static string? TryFindConfigNearGodotProject()
	{
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
			// Best-effort; ignore.
		}

		return null;
	}

	private static string ExpandHomePath(string path)
	{
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
