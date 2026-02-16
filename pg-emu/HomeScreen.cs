using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using PGEmu.app;

public partial class HomeScreen : Control
{
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

	// Card prefab spawned into the carousel.
	[Export] public PackedScene CardScene;

	private Control _cardsRoot;
	private Button _prev;
	private Button _next;
	private Label _selectedTitle;
	private Label _status;
	private Button _selectPlatform;
	private Button _back;
	private Button _friends;
	private Button _chat;
	private Button _settings;
	private Button _help;

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

	private Tween _tween;

	public override void _Ready()
	{
		// Resolve all node references up front; if a NodePath is wrong you'll fail here with a clear error.
		_cardsRoot = GetNode<Control>(CardsPath);
		_prev = GetNode<Button>(PrevPath);
		_next = GetNode<Button>(NextPath);
		_selectedTitle = GetNode<Label>(SelectedTitlePath);
		_status = GetNode<Label>(StatusPath);
		_selectPlatform = GetNode<Button>(SelectPlatformPath);

		_back = GetNodeOrNull<Button>(BackPath);
		_friends = GetNodeOrNull<Button>(FriendsPath);
		_chat = GetNodeOrNull<Button>(ChatPath);
		_settings = GetNodeOrNull<Button>(SettingsPath);
		_help = GetNodeOrNull<Button>(HelpPath);

		_prev.Pressed += () => Step(-1);
		_next.Pressed += () => Step(1);
		_selectPlatform.Pressed += OpenSelectedPlatform;

		if (_back != null) _back.Pressed += OnBackPressed;
		if (_settings != null) _settings.Pressed += OnSettingsPressed;
		if (_friends != null) _friends.Pressed += OnFriendsPressed;
		if (_chat != null) _chat.Pressed += OnChatPressed;
		if (_help != null) _help.Pressed += OnHelpPressed;

		// Load platforms from config, then build the carousel visuals.
		LoadConfigAndPlatforms();
		SpawnCards();
		LayoutCards();
		UpdateSelectedLabel();
	}

	private void OnBackPressed()
	{
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://login.tscn");
	}

	private void OnSettingsPressed()
	{
		// Jump to the Vault (settings) screen. Store the return scene so Vault can go back here.
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);
		tree.ChangeSceneToFile("res://vault.tscn");
	}

	private void OnFriendsPressed()
	{
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://profile.tscn");
	}
	
	private void OnAchPressed(){
		
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://HomeScreen.tscn");
		tree.ChangeSceneToFile("res://Achievements.tscn");
	
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
		if (Count == 0) return;

		var idx = Mathf.RoundToInt(_carouselPos);
		idx = WrapIndex(idx);

		if (idx < 0 || idx >= _platforms.Count) return;
		var platform = _platforms[idx];

		// Pass selection to the next screen without needing a singleton.
		var tree = GetTree();
		tree.SetMeta("pgemu_selected_platform_id", platform.Id);
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);

		tree.ChangeSceneToFile("res://GameSelect.tscn");
	}

	private void SpawnCards()
	{
		// Clear old cards (e.g. after a reload).
		foreach (var c in _cards)
			c.QueueFree();
		_cards.Clear();
		_platforms.Clear();

		if (_config?.Platforms is { Count: > 0 } platforms)
		{
			_platforms.AddRange(platforms);
		}
		else
		{
			// Keep the carousel usable even when config is missing/empty.
			_platforms.Add(new PlatformConfig { Id = "missing", Name = "Missing config.json" });
		}

		foreach (var p in _platforms)
		{
			var card = (Control)CardScene.Instantiate();
			_cardsRoot.AddChild(card);
			_cards.Add(card);

			// `platform_card.tscn` includes a `Panel/Name` label.
			var label = card.GetNodeOrNull<Label>("Panel/Name");
			if (label != null) label.Text = p.Name;
		}

		UpdateSelectedLabel();
		UpdateNavEnabled();
	}

	private int Count => _cards.Count;

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
}
