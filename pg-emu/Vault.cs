using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;

public partial class Vault : Control
{
	private const string LibraryRefreshTokenMeta = "pgemu_library_refresh_token";

	// NodePaths assigned in vault.tscn, keeps UI wiring in-editor instead of hardcoding node strings.
	[Export] public NodePath BackPath;
	[Export] public NodePath LibraryPathEditPath;
	[Export] public NodePath BrowsePath;
	[Export] public NodePath SavePath;
	[Export] public NodePath StatusPath;
	[Export] public NodePath FileDialogPath;

	// Cached scene nodes, resolved in _Ready().
	private Button _back = null!;
	private LineEdit _libraryPathEdit = null!;
	private Button _browse = null!;
	private Button _save = null!;
	private Label _status = null!;
	private FileDialog _fileDialog = null!;

	// Config data and paths.
	private AppConfig _config = new();          // In-memory config (loaded or default).
	private string? _configPath;                // Base config.json path (shared).
	private string? _localConfigPath;           // config.local.json path (user overrides).

	public override void _Ready()
	{
		// Resolve NodePaths into actual nodes.
		_back = GetNode<Button>(BackPath);
		_libraryPathEdit = GetNode<LineEdit>(LibraryPathEditPath);
		_browse = GetNode<Button>(BrowsePath);
		_save = GetNode<Button>(SavePath);
		_status = GetNode<Label>(StatusPath);
		_fileDialog = GetNode<FileDialog>(FileDialogPath);

		ApplyThemeAesthetic();

		// Hook up UI actions.
		_back.Pressed += GoBack;
		_browse.Pressed += OpenBrowse;
		_save.Pressed += SaveConfig;

		// FileDialog emits a directory path when the user picks one.
		_fileDialog.DirSelected += OnDirSelected;

		// Load existing config (or initialize defaults) and populate the UI.
		LoadConfig();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
	}

	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var hint = GetNodeOrNull<Label>("Margin/Root/Body/Hint");
		if (hint != null)
			hint.Text = "Set your library root or your games folder.";
		UiStyle.StyleMetaLabel(hint);

		UiStyle.StyleLineEdit(_libraryPathEdit);
		UiStyle.StylePrimaryButton(_browse);
		UiStyle.StylePrimaryButton(_save);
		UiStyle.TightenButtonContentPadding(_browse, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_save, horizontal: 6f, vertical: 2f);
		UiStyle.StyleStatusLabel(_status);
	}

	private void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}

	private void OpenBrowse()
	{
		AudioManager.Instance?.PlaySelect();
		// Use current text as the starting directory when possible.
		var current = _libraryPathEdit.Text?.Trim();
		if (!string.IsNullOrWhiteSpace(current))
		{
			try
			{
				var normalized = ExpandHomePath(current);
				if (Directory.Exists(normalized))
					_fileDialog.CurrentDir = normalized;
			}
			catch
			{
				// Best-effort only, no need to crash on a bad path string.
			}
		}

		// Show the directory picker.
		_fileDialog.PopupCentered();
	}

	private void OnDirSelected(string dir)
	{
		// Update the textbox with the chosen folder.
		_libraryPathEdit.Text = dir;
		SetStatus($"Selected: {dir}");
	}

	private void LoadConfig()
	{
		try
		{
			var tree = GetTree();

			// Prefer config path passed in from the previous scene, then fall back to heuristics.
			_configPath = tree.HasMeta("pgemu_config_path") ? tree.GetMeta("pgemu_config_path").AsString() : null;
			_configPath = string.IsNullOrWhiteSpace(_configPath) ? null : _configPath;
			_configPath ??= ConfigFinder.FindConfigPath();
			_configPath ??= TryFindConfigNearGodotProject();

			// If we have a real config.json, load it and set up the local override path.
			if (_configPath != null && File.Exists(_configPath))
			{
				_config = AppConfig.Load(_configPath);
				_localConfigPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "config.local.json");

				// Populate UI from loaded config.
				_libraryPathEdit.Text = _config.LibraryRoot;
				SetStatus($"Loaded config: {_configPath}");
				return;
			}

			// No config.json found, start with a blank/default config.
			_config = new AppConfig();
			_libraryPathEdit.Text = "";
			SetStatus("No config.json found; saving will create one.");
		}
		catch (Exception ex)
		{
			// Reset to safe defaults on failure.
			_config = new AppConfig();
			_configPath = null;
			SetStatus($"Load failed: {ex.Message}");
		}
	}

	private void SaveConfig()
	{
		AudioManager.Instance?.PlaySelect();
		try
		{
			// Pull value from UI.
			var raw = _libraryPathEdit.Text?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(raw))
			{
				SetStatus("Library path is empty.");
				return;
			}

			// Normalize and validate the directory.
			var normalized = ExpandHomePath(raw);
			if (!Directory.Exists(normalized))
			{
				SetStatus("That folder doesn't exist.");
				return;
			}

			var normalizedLibraryRoot = LibraryScanner.NormalizeLibraryRoot(normalized, _config.Platforms);
			_config.LibraryRoot = normalizedLibraryRoot;
			_libraryPathEdit.Text = normalizedLibraryRoot;

			// If we don't know where config.json is yet, pick a location near the project.
			_configPath ??= TryFindConfigNearGodotProject(preferCreate: true);
			if (_configPath == null)
			{
				SetStatus("Couldn't determine where to write config.json.");
				return;
			}

			string savePath;
			if (File.Exists(_configPath))
			{
				// Write only the user override to config.local.json so config.json can stay shareable in git.
				_localConfigPath ??= Path.Combine(Path.GetDirectoryName(_configPath)!, "config.local.json");
				var localOverride = new AppConfig
				{
					LibraryRoot = normalizedLibraryRoot
				};
				localOverride.Save(_localConfigPath);
				savePath = _localConfigPath;
			}
			else
			{
				// If there is no base config yet, create one so the rest of the app can actually discover it.
				_config.Save(_configPath);
				savePath = _configPath;
			}

			// Keep base config path in metadata so other scenes can reload consistently.
			var tree = GetTree();
			tree.SetMeta("pgemu_config_path", _configPath);
			tree.SetMeta(LibraryRefreshTokenMeta, DateTime.UtcNow.Ticks);

			// Force the next library/game screen load to rescan instead of reusing stale process-wide caches.
			AchievementStorage.gameToString.Clear();
			AchievementStorage.achievementData = null;
			AchievementStorage.gameId = -1;
			AchievementStorage.gameName = string.Empty;

			var librarySummary = SummarizeLibrary(_config);
			var savedText = string.Equals(normalizedLibraryRoot, normalized, StringComparison.Ordinal)
				? $"Saved: {savePath}"
				: $"Saved library root: {normalizedLibraryRoot}";
			SetStatus(string.IsNullOrWhiteSpace(librarySummary)
				? savedText
				: $"{savedText} • {librarySummary}");
		}
		catch (Exception ex)
		{
			SetStatus($"Save failed: {ex.Message}");
		}
	}

	private void SetStatus(string text)
	{
		// Single place to update the status label.
		_status.Text = text;
	}

	private static string SummarizeLibrary(AppConfig config)
	{
		if (config.Platforms == null || config.Platforms.Count == 0)
			return "No platforms configured.";

		int platformCount = 0;
		int gameCount = 0;

		foreach (var platform in config.Platforms)
		{
			if (platform == null)
				continue;

			try
			{
				var games = LibraryScanner.Scan(platform, config.LibraryRoot, out _);
				if (games.Count == 0)
					continue;

				platformCount++;
				gameCount += games.Count;
			}
			catch
			{
				// Ignore broken platform scans in the summary; save already succeeded.
			}
		}

		if (gameCount == 0)
			return "No games detected yet.";

		return $"Found {gameCount} game{(gameCount == 1 ? "" : "s")} across {platformCount} platform{(platformCount == 1 ? "" : "s")}.";
	}

	private static string? TryFindConfigNearGodotProject(bool preferCreate = false)
	{
		// Best-effort config discovery, and optionally a "where should we create it" decision.
		try
		{
			var projectDir = ProjectSettings.GlobalizePath("res://");

			var inProject = Path.Combine(projectDir, "config.json");
			var inParent = Path.GetFullPath(Path.Combine(projectDir, "..", "config.json"));

			if (!preferCreate)
			{
				// Read mode: only return paths that already exist.
				if (File.Exists(inProject)) return inProject;
				if (File.Exists(inParent)) return inParent;
				return null;
			}

			// Create mode: prefer parent if it already contains a config.json, otherwise default to project dir.
			if (File.Exists(inParent)) return inParent;
			return inProject;
		}
		catch
		{
			// If Godot can't resolve res:// for some reason, just give up quietly.
			return null;
		}
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
