using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;

public partial class GamePathScreen : Control
{
	private const string LibraryRefreshTokenMeta = "pgemu_library_refresh_token";

	// NodePaths assigned in vault.tscn, keeps UI wiring in-editor instead of hardcoding node strings.
	[Export] public NodePath BackPath;
	[Export] public NodePath BrowsePath;
	[Export] public NodePath FileDialogPath;

	// Cached scene nodes, resolved in _Ready().
	private Button _back = null!;
	private Button _browse = null!;
	private FileDialog _fileDialog = null!;
	private LineEdit _curLine = null!;


	// Config data and paths.
	private AppConfig _config = new();          // In-memory config (loaded or default).
	private string? _configPath;                // Base config.json path (shared).
	private string? _localConfigPath;           // config.local.json path (user overrides).

	public override void _Ready()
	{
		
		
		// Resolve NodePaths into actual nodes.
		_back = GetNode<Button>(BackPath);
		_browse = GetNode<Button>(BrowsePath);
		_fileDialog = GetNode<FileDialog>(FileDialogPath);

		VBoxContainer container = GetNode<VBoxContainer>("Margin/Root/Body/ScrollContainer/ButtonContainer");
		GD.Print("hi from after container");
		_fileDialog.DirSelected += OnDirSelected;
		ApplyThemeAesthetic();

		LoadConfig();
		GD.Print(_config.Emulators[0].Name);
		for (int i = 0; i < _config.Platforms.Count; i++)
		{
			int index = i;
			HBoxContainer hbox = new HBoxContainer();
			hbox.CustomMinimumSize = new Vector2(900, 100);
			container.AddChild(hbox);
			Button btn = new Button();
			var platform = _config.Platforms[index];
			btn.Text = platform.Name;
			
			
			btn.CustomMinimumSize = new Vector2(300, 80);

			UiStyle.StyleTopBarButton(btn);  
			
			hbox.AddChild(btn);
			
			VBoxContainer vbox = new VBoxContainer();
			
			hbox.AddChild(vbox);
			
			
			LineEdit line = new LineEdit();
			line.CustomMinimumSize = new Vector2(400, 40 );
			line.Text = platform.RomPath;
			UiStyle.StyleLineEdit(line);
			//Button btnSide = new Button();
			//btnSide.Text = "hi";
			vbox.AddChild(line);
			
			
			Button save = new Button();
			save.Text = "Save Path";
			save.CustomMinimumSize = new Vector2(80, 30 );
			
			save.Pressed += () => {
				_config.Platforms[index].RomPath = line.Text;
				_config.LibraryRoot = "";
				SaveConfig(); 
			};
				
				
			UiStyle.StyleTopBarButton(save);
			
			vbox.AddChild(save);
			
			
			
			Button browse = new Button();
			browse.Text = "Browse";
			browse.CustomMinimumSize = new Vector2(100, 40);
			UiStyle.StyleTopBarButton(browse);
			browse.Pressed += () => {
				_curLine = line;
				OpenBrowse(line);};
			hbox.AddChild(browse);
			
			
			
		}
	}

	
	private void OnDirSelected(string dir)
	{
		// Update the textbox with the chosen folder.
		_curLine.Text = dir;
	//	SetStatus($"Selected: {dir}");
	}

	private void OpenBrowse(LineEdit line)
	{
		AudioManager.Instance?.PlaySelect();
		// Use current text as the starting directory when possible.
		var current = line.Text?.Trim();
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
			hint.Text = "Set your emulator executable paths!";
		UiStyle.StyleMetaLabel(hint);
		
		UiStyle.StylePrimaryButton(_browse);
		UiStyle.TightenButtonContentPadding(_browse, horizontal: 6f, vertical: 2f);
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
				//_localConfigPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "config.local.json");

				
				
				return;
			}

			// No config.json found, start with a blank/default config.
			_config = new AppConfig();
			
			
		}
		catch (Exception ex)
		{
			// Reset to safe defaults on failure.
			_config = new AppConfig();
			_configPath = null;
			
		}
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

	private void SaveConfig()
	{
		AudioManager.Instance?.PlaySelect();
		try
		{
			

			// If we don't know where config.json is yet, pick a location near the project.
			_configPath ??= TryFindConfigNearGodotProject(preferCreate: true);
			if (_configPath == null)
			{
			
				return;
			}

			string savePath;
			if (File.Exists(_configPath))
			{
				_config.Save(_configPath);
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

			
		}
		catch (Exception ex)
		{
			
		}
	}

	

	
}
