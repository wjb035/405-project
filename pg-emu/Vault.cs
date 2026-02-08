using Godot;
using System;
using System.IO;
using PGEmu.app;

public partial class Vault : Control
{
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

		// Hook up UI actions.
		_back.Pressed += GoBack;
		_browse.Pressed += OpenBrowse;
		_save.Pressed += SaveConfig;

		// FileDialog emits a directory path when the user picks one.
		_fileDialog.DirSelected += OnDirSelected;

		// Load existing config (or initialize defaults) and populate the UI.
		LoadConfig();
	}

	private void GoBack()
	{
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}

	private void OpenBrowse()
	{
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

			// Update the in-memory config.
			_config.LibraryRoot = normalized;

			// If we don't know where config.json is yet, pick a location near the project.
			_configPath ??= TryFindConfigNearGodotProject(preferCreate: true);
			if (_configPath == null)
			{
				SetStatus("Couldn't determine where to write config.json.");
				return;
			}

			// Write overrides to config.local.json so config.json can stay shareable in git.
			_localConfigPath ??= Path.Combine(Path.GetDirectoryName(_configPath)!, "config.local.json");
			var savePath = _localConfigPath ?? _configPath;
			_config.Save(savePath);

			// Keep base config path in metadata so other scenes can reload consistently.
			var tree = GetTree();
			tree.SetMeta("pgemu_config_path", _configPath);

			SetStatus($"Saved: {savePath}");
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
