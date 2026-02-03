using Godot;
using System;
using System.IO;
using PGEmu.app;

public partial class Vault : Control
{
	// Scene wiring (assigned in `vault.tscn`).
	[Export] public NodePath BackPath;
	[Export] public NodePath LibraryPathEditPath;
	[Export] public NodePath BrowsePath;
	[Export] public NodePath SavePath;
	[Export] public NodePath StatusPath;
	[Export] public NodePath FileDialogPath;

	private Button _back = null!;
	private LineEdit _libraryPathEdit = null!;
	private Button _browse = null!;
	private Button _save = null!;
	private Label _status = null!;
	private FileDialog _fileDialog = null!;

	private AppConfig _config = new();
	private string? _configPath;
	private string? _localConfigPath;

	public override void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		_libraryPathEdit = GetNode<LineEdit>(LibraryPathEditPath);
		_browse = GetNode<Button>(BrowsePath);
		_save = GetNode<Button>(SavePath);
		_status = GetNode<Label>(StatusPath);
		_fileDialog = GetNode<FileDialog>(FileDialogPath);

		_back.Pressed += GoBack;
		_browse.Pressed += OpenBrowse;
		_save.Pressed += SaveConfig;

		_fileDialog.DirSelected += OnDirSelected;

		LoadConfig();
	}

	private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}

	private void OpenBrowse()
	{
		// Use current value as starting dir when possible.
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
				// ignore
			}
		}

		_fileDialog.PopupCentered();
	}

	private void OnDirSelected(string dir)
	{
		_libraryPathEdit.Text = dir;
		SetStatus($"Selected: {dir}");
	}

	private void LoadConfig()
	{
		try
		{
			var tree = GetTree();

			_configPath = tree.HasMeta("pgemu_config_path") ? tree.GetMeta("pgemu_config_path").AsString() : null;
			_configPath = string.IsNullOrWhiteSpace(_configPath) ? null : _configPath;
			_configPath ??= ConfigFinder.FindConfigPath();
			_configPath ??= TryFindConfigNearGodotProject();

			if (_configPath != null && File.Exists(_configPath))
			{
				_config = AppConfig.Load(_configPath);
				_localConfigPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "config.local.json");
				_libraryPathEdit.Text = _config.LibraryRoot;
				SetStatus($"Loaded config: {_configPath}");
				return;
			}

			// No config yet: initialize to a sensible default.
			_config = new AppConfig();
			_libraryPathEdit.Text = "";
			SetStatus("No config.json found; saving will create one.");
		}
		catch (Exception ex)
		{
			_config = new AppConfig();
			_configPath = null;
			SetStatus($"Load failed: {ex.Message}");
		}
	}

	private void SaveConfig()
	{
		try
		{
			var raw = _libraryPathEdit.Text?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(raw))
			{
				SetStatus("Library path is empty.");
				return;
			}

			var normalized = ExpandHomePath(raw);
			if (!Directory.Exists(normalized))
			{
				SetStatus("That folder doesn't exist.");
				return;
			}

			_config.LibraryRoot = normalized;

			// If we don't have a base config path yet, create config.json in the project root (or parent if present).
			_configPath ??= TryFindConfigNearGodotProject(preferCreate: true);
			if (_configPath == null)
			{
				SetStatus("Couldn't determine where to write config.json.");
				return;
			}

			// Save user-specific overrides to config.local.json (keeps config.json shareable).
			_localConfigPath ??= Path.Combine(Path.GetDirectoryName(_configPath)!, "config.local.json");
			var savePath = _localConfigPath ?? _configPath;
			_config.Save(savePath);

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
		_status.Text = text;
	}

	private static string? TryFindConfigNearGodotProject(bool preferCreate = false)
	{
		try
		{
			var projectDir = ProjectSettings.GlobalizePath("res://");

			var inProject = Path.Combine(projectDir, "config.json");
			var inParent = Path.GetFullPath(Path.Combine(projectDir, "..", "config.json"));

			if (!preferCreate)
			{
				if (File.Exists(inProject)) return inProject;
				if (File.Exists(inParent)) return inParent;
				return null;
			}

			// Prefer writing to parent if it already looks like the repo root.
			if (File.Exists(inParent)) return inParent;
			return inProject;
		}
		catch
		{
			return null;
		}
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
