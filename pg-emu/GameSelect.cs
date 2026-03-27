using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PGEmu.app;
using System.Threading.Tasks;
using RetroAchievements.Api;
using PGEmu.Services;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;


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
	[Export] public NodePath AddPath;


	// Prefab for a single carousel card.
	[Export] public PackedScene CardScene;

	// Cached scene nodes, resolved in _Ready().
	private Control _carouselArea = null!;
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
	private Button _friends = null!;
	private Button _add = null!;
	private PanelContainer _listShell = null!;
	private ScrollContainer _listScroll = null!;
	private VBoxContainer _listRows = null!;
	private PanelContainer _gridShell = null!;
	private ScrollContainer _gridScroll = null!;
	private GridContainer _gridRows = null!;
	OptionButton _optionButton = new OptionButton();
	
	private Button _achievement = null!;

	// UI instances and data backing the carousel.
	private readonly List<Control> _cards = new();
	private readonly List<Control> _browseEntries = new();
	private readonly List<GameEntry> _games = new();
	private static readonly System.Net.Http.HttpClient CoverArtClient = new();
	private static readonly Dictionary<string, Texture2D> CoverArtCache = new(StringComparer.OrdinalIgnoreCase);
	private BrowseLayoutMode _browseLayout = BrowseLayoutMode.Carousel;
	private int _selectedIndex;
	private const int GridColumns = 4;
	private const float CarouselCardSpacing = 404f;

	
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
	private int _verticalAxisDir;
	private long _verticalAxisNextMs;

	// Active snap tween, killed on new input to keep things responsive.
	private Tween? _tween;
	
	// 3D CAROUSEL MODE
	private Carousel3DView? _carousel3D;

	public override async void _Ready()
	{
		
		// Resolve exported node paths into actual nodes.
		_carouselArea = GetNode<Control>("Margin/Root/CenterArea/CarouselArea");
		_cardsRoot = GetNode<Control>(CardsPath);
		_prev = GetNode<Button>(PrevPath);
		_next = GetNode<Button>(NextPath);
		_title = GetNode<Label>(TitlePath);
		_metaLeft = GetNode<Label>(MetaLeftPath);
		_metaRight = GetNode<Label>(MetaRightPath);
		_status = GetNode<Label>(StatusPath);
		_status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_status.ClipText = true;
		_back = GetNode<Button>(BackPath);
		_play = GetNode<Button>(PlayPath);
		_settings = GetNode<Button>(SettingsPath);
		_friends = GetNode<Button>("Margin/Root/TopBar/TopIcons/BtnFriends");
		_achievement = GetNode<Button>("Margin/Root/TopBar/TopIcons/BtnAch");
		_add = GetNode<Button>(AddPath);
		CreateAlternateLayoutViews();
		_browseLayout = BrowseLayoutSettings.GetLayout();
			
		// DEBUG
		GD.Print($"CarouselArea size: {_carouselArea.Size}, position: {_carouselArea.Position}");
		
		
		_optionButton.Name = "test";
		var container = GetNode<HBoxContainer>("Margin/Root/CenterArea/HBoxContainer");
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
		_friends.Pressed += OpenProfile;
		_add.Pressed += addToCollection;
		ApplyAesthetic();
		

		// background transition
		StartBackgroundTransition();
		
		ConnectAllButtons(this);
		GD.Print("IN GAME SELECT");
		InputRoutingService.Instance?.UnlockUiInput();
		// Load data and build UI.
		await LoadContextAndGames();
		SpawnCards();
		ApplyBrowseLayoutMode();
		LayoutCards();
		UpdateSelectionUI();
		_achievement.Show();
		
		
	}

private void ConnectAllButtons(Node node)
{
	foreach (Node child in node.GetChildren())
	{
		if (child is Button button &&
			button != _prev &&
			button != _next &&
			button != _play &&
			button != _back &&
			button != _settings &&
			button != _friends &&
			button != _achievement)
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


	private void GoBack()
	{
		CollectionStorage.currentCollection = null;
		AudioManager.Instance?.PlayNavigation(-1);
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
		AudioManager.Instance?.PlayNavigation(1);
		// Store return context in SceneTree meta so Settings can return here with the same config.
		var tree = GetTree();
		tree.SetMeta("pgemu_return_scene", "res://GameSelect.tscn");
		tree.SetMeta("pgemu_settings_tab", "appearance");
		if (_configPath != null)
			tree.SetMeta("pgemu_config_path", _configPath);

		tree.ChangeSceneToFile("res://Settings.tscn");
	}

	private void OpenProfile()
	{
		AudioManager.Instance?.PlayNavigation(1);
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
		AudioManager.Instance?.PlayNavigation(1);
		AchievementStorage.gameName = GetSelectedGame().Name;
		AchievementStorage.gameId = GetSelectedGame().retroAchievementsGameId;
		GetTree().ChangeSceneToFile("res://Achievements.tscn");
	}

	private void PlaySelected()
	{
		if (ShouldIgnoreUiInput())
			return;

		var game = GetSelectedGame();
		if (game == null || string.IsNullOrWhiteSpace(game.Path))
		{
			SetStatus("No game selected.");
			return;
		}

		AudioManager.Instance?.PlaySelect();

		// Use platform from current screen, unless we are launching from a collection entry.
		var launchPlatform = CollectionStorage.currentCollection == null ? _platform : game.platform;
		if (_config == null || launchPlatform == null)
		{
			SetStatus("Can't launch: config or platform missing.");
			return;
		}
		
		var authService = GetNode<AuthService>("/root/AuthService");
		var uid = authService.getUID();
		GD.Print("Current user ID: " + uid);
		
		try
		{
			if (TryStartInProcessLaunch(_config, launchPlatform, game, out var inProcessStatus))
			{
				SetStatus(inProcessStatus);
				return;
			}
			
			//GetNode<Playtime>("/root/Playtime").killCur();
			
			
			Launcher.LaunchFromConfig(_config, launchPlatform, game);
			//runningProcesses = Process.GetProcessesByName("dolphin");
			GetNode<Playtime>("/root/Playtime").FindPlatform(launchPlatform, game);
			
			
			foreach (var child in GetTree().Root.GetChildren())
				GD.Print(child.Name);
			
	 ActivityManager.SetActivity("Playing", game.Title, "GodotClient");

		//	foreach (var p in runningProcesses){
			//GD.Print(p.ProcessName +  " started at " + p.StartTime);
				
			//}
			
			
			InputRoutingService.Instance?.LockUiInputForExternalLaunch();
			SetStatus($"Launching external emulator: {game.Title}");
		}
		catch (Exception ex)
		{
			InputRoutingService.Instance?.UnlockUiInput();
			SetStatus($"Launch failed: {ex.Message}");
		}
	}

	private bool TryStartInProcessLaunch(AppConfig cfg, PlatformConfig platform, GameEntry game, out string status)
	{
		status = string.Empty;

		var emulatorId = platform.DefaultEmulatorId ?? cfg.Emulators.FirstOrDefault()?.Id;
		if (string.IsNullOrWhiteSpace(emulatorId))
			return false;

		var emulator = cfg.Emulators.FirstOrDefault(e =>
			string.Equals(e.Id, emulatorId, StringComparison.OrdinalIgnoreCase));
		if (emulator == null)
			return false;

		var configuredCorePath = SelectPlatformPath(
			emulator.CorePath,
			emulator.CorePathWindows,
			emulator.CorePathMac,
			emulator.CorePathLinux);

		if (string.IsNullOrWhiteSpace(configuredCorePath))
			return false;

		var fullCorePath = ResolveConfigPath(cfg, configuredCorePath, allowDirectory: false);
		if (fullCorePath == null || !File.Exists(fullCorePath))
			throw new FileNotFoundException($"Libretro core not found. corePath='{configuredCorePath}'.");

		if (string.Equals(emulator.Id, "dolphin", StringComparison.OrdinalIgnoreCase))
		{
			PrepareDolphinCoreAssets(cfg, emulator, fullCorePath);
		}

		InProcessLaunchState.SetPending(new InProcessLaunchRequest
		{
			RomPath = game.Path,
			CorePath = fullCorePath,
			CoreId = emulator.Id,
			GameTitle = game.Title,
			ReturnScene = "res://GameSelect.tscn",
		});

		InputRoutingService.Instance?.UnlockUiInput();
		GetTree().ChangeSceneToFile("res://LibretroGame.tscn");
		status = $"Launching in-app core: {Path.GetFileName(fullCorePath)}";
		return true;
	}

	private static readonly string[] DolphinRequiredSysFiles =
	{
		Path.Combine("GC", "dsp_coef.bin"),
		Path.Combine("GC", "dsp_rom.bin"),
		Path.Combine("GC", "font_japanese.bin"),
		Path.Combine("GC", "font_western.bin"),
	};

	private void PrepareDolphinCoreAssets(AppConfig cfg, EmulatorConfig emulator, string fullCorePath)
	{
		var systemRoot = ProjectSettings.GlobalizePath("user://system/");
		var saveRoot = ProjectSettings.GlobalizePath("user://saves/");
		var coreDir = Path.GetDirectoryName(fullCorePath) ?? string.Empty;
		var cwd = System.Environment.CurrentDirectory;

		var sysTargets = new[]
		{
			// Seed every common lookup location used by libretro Dolphin builds.
			Path.Combine(systemRoot, "dolphin-emu", "Sys"),
			Path.Combine(systemRoot, "Sys"),
			!string.IsNullOrWhiteSpace(coreDir) ? Path.Combine(coreDir, "dolphin-emu", "Sys") : null,
			!string.IsNullOrWhiteSpace(coreDir) ? Path.Combine(coreDir, "Sys") : null,
			!string.IsNullOrWhiteSpace(cwd) ? Path.Combine(cwd, "dolphin-emu", "Sys") : null,
			!string.IsNullOrWhiteSpace(cwd) ? Path.Combine(cwd, "Sys") : null,
		}
		
		.Where(p => !string.IsNullOrWhiteSpace(p))
		.Cast<string>()
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToArray();

		var missingSysTargets = sysTargets.Where(target => !HasDolphinSysAssets(target)).ToArray();
			if (missingSysTargets.Length > 0)
			{
				var sourceSys = FindDolphinSysSource(cfg, emulator, fullCorePath);
				if (!string.IsNullOrWhiteSpace(sourceSys))
			{
				foreach (var target in missingSysTargets)
				{
					CopyDirectoryIfMissing(sourceSys!, target);
				}
			}
			else
			{
					GD.PrintErr("[DolphinSetup] Could not find a Dolphin Sys source directory.");
				}
			}

			// Some Dolphin builds look for ansi/sjis font names, others use western/japanese.
			foreach (var target in sysTargets)
			{
				EnsureDolphinFontAliases(target);
			}

		var userTemplate = Path.Combine(coreDir, "User");
		var userTargets = new[]
		{
			// Mirror user profile layout in app data and core-relative fallbacks.
			Path.Combine(saveRoot, "User"),
			Path.Combine(saveRoot, "dolphin-emu", "User"),
			!string.IsNullOrWhiteSpace(coreDir) ? Path.Combine(coreDir, "User") : null,
			!string.IsNullOrWhiteSpace(coreDir) ? Path.Combine(coreDir, "dolphin-emu", "User") : null,
			!string.IsNullOrWhiteSpace(cwd) ? Path.Combine(cwd, "User") : null,
			!string.IsNullOrWhiteSpace(cwd) ? Path.Combine(cwd, "dolphin-emu", "User") : null,
		}
		.Where(p => !string.IsNullOrWhiteSpace(p))
		.Cast<string>()
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToArray();

		if (Directory.Exists(userTemplate))
		{
			foreach (var target in userTargets)
			{
				CopyDirectoryIfMissing(userTemplate, target);
				PatchDolphinUserConfig(target);
			}
		}
	}

	private static void PatchDolphinUserConfig(string userRoot)
	{
		if (string.IsNullOrWhiteSpace(userRoot))
			return;

		var configDir = Path.Combine(userRoot, "Config");
		Directory.CreateDirectory(configDir);
		var dolphinIniPath = Path.Combine(configDir, "Dolphin.ini");

		var lines = File.Exists(dolphinIniPath)
			? File.ReadAllLines(dolphinIniPath).ToList()
			: new List<string>();

		// Conservative settings for embedded Dolphin core stability.
		UpsertIniValue(lines, "Core", "CPUThread", "True");
		UpsertIniValue(lines, "Core", "Fastmem", "False");
		UpsertIniValue(lines, "Core", "FastmemArena", "False");
		UpsertIniValue(lines, "Core", "SkipIPL", "True");
		UpsertIniValue(lines, "DSP", "DSPThread", "False");

		File.WriteAllLines(dolphinIniPath, lines);
	}

	private static void UpsertIniValue(List<string> lines, string section, string key, string value)
	{
		string sectionHeader = $"[{section}]";
		int sectionStart = -1;
		int sectionEnd = lines.Count;

		for (int i = 0; i < lines.Count; i++)
		{
			var trimmed = lines[i].Trim();
			if (sectionStart < 0)
			{
				if (string.Equals(trimmed, sectionHeader, StringComparison.OrdinalIgnoreCase))
				{
					sectionStart = i;
				}
				continue;
			}

			if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
			{
				sectionEnd = i;
				break;
			}
		}

		if (sectionStart < 0)
		{
			if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
				lines.Add(string.Empty);
			lines.Add(sectionHeader);
			lines.Add($"{key} = {value}");
			return;
		}

		for (int i = sectionStart + 1; i < sectionEnd; i++)
		{
			var trimmed = lines[i].TrimStart();
			if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
				continue;

			int equalsIndex = trimmed.IndexOf('=');
			if (equalsIndex < 0)
				continue;

			lines[i] = $"{key} = {value}";
			return;
		}

		lines.Insert(sectionEnd, $"{key} = {value}");
	}

	private static string? FindDolphinSysSource(AppConfig cfg, EmulatorConfig emulator, string fullCorePath)
	{
		var coreDir = Path.GetDirectoryName(fullCorePath);
		var configuredExePath = SelectPlatformPath(
			emulator.ExePath,
			emulator.ExePathWindows,
			emulator.ExePathMac,
			emulator.ExePathLinux);
		var resolvedExePath = !string.IsNullOrWhiteSpace(configuredExePath)
			? ResolveConfigPath(cfg, configuredExePath!, allowDirectory: true)
			: null;

		var candidates = new List<string>();

		if (!string.IsNullOrWhiteSpace(coreDir))
		{
			candidates.Add(Path.Combine(coreDir!, "Sys"));
			candidates.Add(Path.Combine(coreDir!, "dolphin-emu", "Sys"));
		}

		if (!string.IsNullOrWhiteSpace(resolvedExePath))
		{
			var appBundle = TryGetMacAppBundlePath(resolvedExePath!);
			if (!string.IsNullOrWhiteSpace(appBundle))
				candidates.Add(Path.Combine(appBundle!, "Contents", "Resources", "Sys"));
		}

		if (!string.IsNullOrWhiteSpace(cfg.LibraryRoot))
			candidates.Add(Path.Combine(ExpandHomePath(cfg.LibraryRoot), "Dolphin.app", "Contents", "Resources", "Sys"));

		candidates.Add("/Applications/Dolphin.app/Contents/Resources/Sys");

		foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (HasDolphinSysAssets(candidate))
				return candidate;
		}

		return null;
	}

	private static string? TryGetMacAppBundlePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return null;

		if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
			return path;

		var idx = path.LastIndexOf(".app", StringComparison.OrdinalIgnoreCase);
		if (idx < 0)
			return null;

		return path.Substring(0, idx + 4);
	}

	private static bool HasDolphinSysAssets(string sysDir)
	{
		if (string.IsNullOrWhiteSpace(sysDir) || !Directory.Exists(sysDir))
			return false;

		foreach (var relative in DolphinRequiredSysFiles)
		{
			var candidate = Path.Combine(sysDir, relative);
			if (!File.Exists(candidate))
				return false;
		}

		return true;
	}

	private static void CopyDirectoryIfMissing(string sourceDir, string destinationDir)
	{
		Directory.CreateDirectory(destinationDir);

		foreach (var file in Directory.GetFiles(sourceDir))
		{
			var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
			if (!File.Exists(destinationFile))
				File.Copy(file, destinationFile, overwrite: false);
		}

		foreach (var childDir in Directory.GetDirectories(sourceDir))
		{
			var destinationChild = Path.Combine(destinationDir, Path.GetFileName(childDir));
			CopyDirectoryIfMissing(childDir, destinationChild);
		}
	}

	private static void EnsureDolphinFontAliases(string sysDir)
	{
		if (string.IsNullOrWhiteSpace(sysDir))
			return;

		var gcDir = Path.Combine(sysDir, "GC");
		if (!Directory.Exists(gcDir))
			return;

		// Accept either canonical Dolphin names or libretro alias names.
		CopyIfMissing(Path.Combine(gcDir, "font_western.bin"), Path.Combine(gcDir, "font_ansi.bin"));
		CopyIfMissing(Path.Combine(gcDir, "font_japanese.bin"), Path.Combine(gcDir, "font_sjis.bin"));
		CopyIfMissing(Path.Combine(gcDir, "font_ansi.bin"), Path.Combine(gcDir, "font_western.bin"));
		CopyIfMissing(Path.Combine(gcDir, "font_sjis.bin"), Path.Combine(gcDir, "font_japanese.bin"));
	}

	private static void CopyIfMissing(string source, string destination)
	{
		if (File.Exists(destination) || !File.Exists(source))
			return;

		File.Copy(source, destination, overwrite: false);
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
					var prevGames = ((IEnumerable<GameEntry>)AchievementStorage.gameToString[_platform.retroachievementsPlatformID]).ToList();

					if (prevGames.Count == 0 && scanned.Count > 0)
					{
						AchievementStorage.gameToString.Remove(_platform.retroachievementsPlatformID);
						foreach (var g in scanned){
							_games.Add(g);
						}
					}
					else
					{
						foreach (var g in prevGames){
							_games.Add(g);
						}
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
			
			
			
			await Task.WhenAll(
				LibretroThumbnailService.PopulateCoverArtAsync(_platform, _games),
				RetroAchievementsService.Retro(_platform, _games));
			
			
			
			if (_games.Count == 0)
			{
				GD.Print($"No games found for {_platform.Name}. Dir='{scanDir}', Extensions=[{string.Join(", ", _platform.Extensions)}].");
				SetStatus(
					$"No games found for {_platform.Name}.");
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
		// Destroy existing browse nodes before rebuilding.
		foreach (var c in _cards)
			c.QueueFree();
		_cards.Clear();
		foreach (Node child in _listRows.GetChildren())
			child.QueueFree();
		foreach (Node child in _gridRows.GetChildren())
			child.QueueFree();
		_browseEntries.Clear();

		// Keep the carousel visible even with zero results by injecting a placeholder entry.
		if (_games.Count == 0)
		{
			var placeholder = new GameEntry { Name = "No games found", Path = "" };
			_games.Add(placeholder);
		}

		_selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, _games.Count - 1));
		_carouselPos = WrapPos(_selectedIndex);

		if (_browseLayout == BrowseLayoutMode.List)
		{
			BuildListRows();
		}
		else if (_browseLayout == BrowseLayoutMode.Grid)
		{
			BuildGridTiles();
		}
		else
		{
			BuildCarouselCards();
		}
		
		if (_carousel3D != null)
		{
			var gameData = _games.Select(g =>
			{
				Texture2D? tex = null;
				if (!string.IsNullOrWhiteSpace(g.CoverArtUrl) &&
					CoverArtCache.TryGetValue(g.CoverArtUrl, out var cached))
					tex = cached;
				GD.Print($"3D populate: {g.Title} → tex={tex != null}"); 
				return (g.Title, tex);
			}).ToList();
			_carousel3D.Populate(gameData, _carouselPos);
		}

		UpdateNavEnabled();
	}

	private int Count => _games.Count;

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
		if (Count <= 1) return;
		AudioManager.Instance?.PlayNavigation(dir);

		if (_browseLayout == BrowseLayoutMode.Carousel)
		{
			SnapTo(_carouselPos + dir, overshoot: true);
			return;
		}

		if (_browseLayout == BrowseLayoutMode.ThreeD)
		{
			// Push velocity directly into the 3D carousel
			if (_carousel3D != null)
				_carousel3D.StepDirection(dir);
			return;
		}

		SetSelectedIndex(Mathf.Clamp(_selectedIndex + dir, 0, Count - 1));
	}

	private void StepVertical(int dir)
	{
		if (Count <= 1) return;

		if (_browseLayout == BrowseLayoutMode.Grid)
		{
			AudioManager.Instance?.PlayNavigation(dir);
			SetSelectedIndex(Mathf.Clamp(_selectedIndex + (dir * GridColumns), 0, Count - 1));
			return;
		}

		Step(dir);
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
			_selectedIndex = WrapIndex(Mathf.RoundToInt(_carouselPos));
			LayoutCards();
			UpdateSelectionUI();
		}));
	}

	public override void _Process(double delta)
	{
		if (_browseLayout == BrowseLayoutMode.Carousel)
			LayoutCards();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (ShouldIgnoreUiInput()) return;
		if (_browseLayout != BrowseLayoutMode.Carousel) return;
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

			// Keep drag distance aligned with the actual carousel spacing.
			_carouselPos = WrapPos(_dragStartCarouselPos - (dx / CarouselCardSpacing));

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
				if (Count > 1)
				{
					Step(-1);
					MarkInputHandled();
				}
				break;
			case JoyButton.RightShoulder:
				if (Count > 1)
				{
					Step(1);
					MarkInputHandled();
				}
				break;
			case JoyButton.DpadLeft:
				if (Count > 1)
				{
					Step(-1);
					MarkInputHandled();
				}
				break;
			case JoyButton.DpadRight:
				if (Count > 1)
				{
					Step(1);
					MarkInputHandled();
				}
				break;
			case JoyButton.DpadUp:
				if (Count > 1)
				{
					StepVertical(-1);
					MarkInputHandled();
				}
				break;
			case JoyButton.DpadDown:
				if (Count > 1)
				{
					StepVertical(1);
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
		if (_browseLayout == BrowseLayoutMode.Carousel)
		{
			if (jm.Axis == JoyAxis.LeftX)
				return HandleAxis(jm.AxisValue, ref _leftAxisDir, ref _leftAxisNextMs, Step);
			return false;
		}

		if (_browseLayout == BrowseLayoutMode.Grid && jm.Axis == JoyAxis.LeftX)
			return HandleAxis(jm.AxisValue, ref _leftAxisDir, ref _leftAxisNextMs, Step);

		if (jm.Axis == JoyAxis.LeftY)
			return HandleAxis(jm.AxisValue, ref _verticalAxisDir, ref _verticalAxisNextMs, StepVertical);

		return false;
	}

	private bool HandleAxis(float value, ref int heldDir, ref long nextMs, Action<int> stepAction)
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
			stepAction(dir);
			heldDir = dir;
			nextMs = now + AxisRepeatMs;
			return true;
		}

		return false;
	}

	private void LayoutCards()
	{
		if (_browseLayout != BrowseLayoutMode.Carousel) return;
		if (Count == 0 || _cards.Count == 0) return;

		// Center of the cards container.
		var center = _cardsRoot.Size * 0.5f;

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
			var x = center.X + d * CarouselCardSpacing;
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
		if (_games.Count == 0) return null;
		var idx = _browseLayout == BrowseLayoutMode.Carousel
			? WrapIndex(Mathf.RoundToInt(_carouselPos))
			: Mathf.Clamp(_selectedIndex, 0, _games.Count - 1);
		if (idx < 0 || idx >= _games.Count) return null;
		return _games[idx];
	}

	private void UpdateSelectionUI()
	{
		var game = GetSelectedGame();
		var hasGames = GetActualGameCount() > 0;
		_title.Text = hasGames ? game?.Title ?? "" : "No games found";

		var platformName = _platform?.Name ?? "Unknown Platform";
		_metaLeft.Text = $"{platformName} • {GetActualGameCount()} game(s) • {BrowseLayoutSettings.GetLabel(_browseLayout)}";
		_metaRight.Text = hasGames && game != null ? $"Achievements: {BuildAchievementDisplay(game)}" : "";
		RefreshBrowseSelection();
		_play.Disabled = _config == null || _platform == null || game == null || string.IsNullOrEmpty(game.Path);
		if (CollectionStorage.currentCollection != null && game != null && !string.IsNullOrEmpty(game.Path))
		{
			_play.Disabled = false;
		}
	}

	private void UpdateNavEnabled()
	{
		// Only allow carousel nav when there are multiple real games to move through.
		var enabled = (_browseLayout == BrowseLayoutMode.Carousel || _browseLayout == BrowseLayoutMode.ThreeD) && GetActualGameCount() > 1;
		_prev.Visible = enabled;
		_next.Visible = enabled;
		_prev.Disabled = !enabled;
		_next.Disabled = !enabled;
	}

	private void SetStatus(string text)
	{
		_status.Text = text;
	}

	private void CreateAlternateLayoutViews()
	{
		_listShell = CreateBrowseShell("ListShell");
		_listScroll = new ScrollContainer
		{
			Name = "ListScroll",
			LayoutMode = 1,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		_listScroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_listRows = new VBoxContainer
		{
			Name = "ListRows",
			LayoutMode = 2,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		_listRows.AddThemeConstantOverride("separation", 14);
		_listScroll.AddChild(_listRows);
		_listShell.AddChild(_listScroll);
		_carouselArea.AddChild(_listShell);
		_carouselArea.MoveChild(_listShell, 1);

		_gridShell = CreateBrowseShell("GridShell");
		_gridScroll = new ScrollContainer
		{
			Name = "GridScroll",
			LayoutMode = 1,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		_gridScroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_gridRows = new GridContainer
		{
			Name = "GridRows",
			LayoutMode = 2,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			Columns = GridColumns,
		};
		_gridRows.AddThemeConstantOverride("h_separation", 18);
		_gridRows.AddThemeConstantOverride("v_separation", 18);
		_gridScroll.AddChild(_gridRows);
		_gridShell.AddChild(_gridScroll);
		
		_carouselArea.AddChild(_gridShell);
		_carouselArea.MoveChild(_gridShell, 2);
		_carousel3D = new Carousel3DView { Name = "Carousel3D", Visible = false };
		_carousel3D.SelectionChanged += idx => SetSelectedIndex(idx);
		_carousel3D.LayoutMode = 1;
		_carousel3D.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_carousel3D.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_carousel3D.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_carouselArea.AddChild(_carousel3D);
		_carousel3D.MouseFilter = Control.MouseFilterEnum.Pass;
	}

	private PanelContainer CreateBrowseShell(string name)
	{
		var shell = new PanelContainer
		{
			Name = name,
			LayoutMode = 1,
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		shell.OffsetLeft = 120f;
		shell.OffsetTop = 72f;
		shell.OffsetRight = -120f;
		shell.OffsetBottom = -22f;
		shell.AddThemeStyleboxOverride("panel", CreatePanelStyleBox(
			new Color(0.08f, 0.06f, 0.14f, 0.94f),
			new Color(0.64f, 0.52f, 0.88f, 0.72f),
			borderWidth: 2,
			radius: 28,
			contentMarginLeft: 22f,
			contentMarginTop: 22f,
			contentMarginRight: 22f,
			contentMarginBottom: 22f));
		return shell;
	}

	private static StyleBoxFlat CreatePanelStyleBox(
		Color background,
		Color border,
		int borderWidth,
		int radius,
		float contentMarginLeft = 0f,
		float contentMarginTop = 0f,
		float contentMarginRight = 0f,
		float contentMarginBottom = 0f)
	{
		return new StyleBoxFlat
		{
			BgColor = background,
			BorderColor = border,
			BorderBlend = true,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomRight = radius,
			CornerRadiusBottomLeft = radius,
			ContentMarginLeft = contentMarginLeft,
			ContentMarginTop = contentMarginTop,
			ContentMarginRight = contentMarginRight,
			ContentMarginBottom = contentMarginBottom,
		};
	}

	private void BuildCarouselCards()
	{
		GD.Print($"BuildCarouselCards called, game count: {_games.Count}");
		foreach (var g in _games)
		{
			var card = (Control)CardScene.Instantiate();
			_cardsRoot.AddChild(card);
			_cards.Add(card);

			var label = card.GetNodeOrNull<Label>("Panel/Name");
			if (label != null) label.Text = g.Title;

			var coverArt = card.GetNodeOrNull<TextureRect>("Panel/CoverArt");
			if (coverArt != null && label != null)
			{
				coverArt.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
				coverArt.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				GD.Print($"Binding cover art for {g.Title}, url={g.CoverArtUrl}"); 
				BindCoverArt(coverArt, label, g);
			}
		}
	}

	private void BuildListRows()
	{
		if (GetActualGameCount() == 0)
		{
			BuildListEmptyState();
			return;
		}

		for (int i = 0; i < _games.Count; i++)
		{
			var row = CreateSelectableEntry(i, gridStyle: false);
			row.CustomMinimumSize = new Vector2(0f, 174f);
			row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			var margin = new MarginContainer
			{
				Name = "RowMargin",
				LayoutMode = 2,
			};
			margin.AddThemeConstantOverride("margin_left", 18);
			margin.AddThemeConstantOverride("margin_top", 14);
			margin.AddThemeConstantOverride("margin_right", 18);
			margin.AddThemeConstantOverride("margin_bottom", 14);

			var split = new HBoxContainer
			{
				Name = "RowSplit",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				Alignment = BoxContainer.AlignmentMode.Begin,
			};
			split.AddThemeConstantOverride("separation", 18);

			var stripe = new ColorRect
			{
				Name = "AccentBar",
				CustomMinimumSize = new Vector2(6f, 0f),
				LayoutMode = 2,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
				Color = new Color(0.44f, 0.31f, 0.70f, 0.58f),
			};

			var posterShell = new PanelContainer
			{
				Name = "PosterShell",
				CustomMinimumSize = new Vector2(132f, 146f),
				LayoutMode = 2,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			};

			var posterMargin = new MarginContainer
			{
				LayoutMode = 2,
			};
			posterMargin.AddThemeConstantOverride("margin_left", 6);
			posterMargin.AddThemeConstantOverride("margin_top", 6);
			posterMargin.AddThemeConstantOverride("margin_right", 6);
			posterMargin.AddThemeConstantOverride("margin_bottom", 6);

			var posterCenter = new CenterContainer
			{
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			};

			var posterArt = new TextureRect
			{
				Name = "PosterArt",
				LayoutMode = 2,
				CustomMinimumSize = new Vector2(120f, 134f),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			};

			var posterMonogram = new Label
			{
				Name = "PosterMonogram",
				LayoutMode = 2,
				Text = BuildGameMonogram(_games[i]),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			posterMonogram.AddThemeColorOverride("font_color", new Color(0.94f, 0.92f, 1f, 0.96f));
			posterMonogram.AddThemeFontSizeOverride("font_size", 34);
			BindCoverArt(posterArt, posterMonogram, _games[i]);

			var textColumn = new VBoxContainer
			{
				Name = "TextColumn",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			textColumn.AddThemeConstantOverride("separation", 6);

			var titleRow = new HBoxContainer
			{
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			titleRow.AddThemeConstantOverride("separation", 12);

			var title = new Label
			{
				Name = "TitleLabel",
				LayoutMode = 2,
				Text = _games[i].Title,
				HorizontalAlignment = HorizontalAlignment.Left,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			title.AddThemeColorOverride("font_color", new Color(0.96f, 0.94f, 1f, 0.98f));
			title.AddThemeFontSizeOverride("font_size", 25);

			var statusBadge = BuildGameBadge(_games[i]);
			PanelContainer? statusChip = string.IsNullOrWhiteSpace(statusBadge)
				? null
				: CreateChip(statusBadge, new Color(0.20f, 0.29f, 0.36f, 0.90f), "StateChip", 11);

			var footerRow = new HBoxContainer
			{
				Name = "FooterRow",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			footerRow.AddThemeConstantOverride("separation", 10);

			var playtime = new Label
			{
				Name = "PlaytimeLabel",
				LayoutMode = 2,
				Text = BuildPlaytimeSummary(_games[i]),
				HorizontalAlignment = HorizontalAlignment.Left,
			};
			playtime.AddThemeColorOverride("font_color", new Color(0.72f, 0.90f, 1f, 0.88f));
			playtime.AddThemeFontSizeOverride("font_size", 13);

			var actionShell = new PanelContainer
			{
				Name = "ActionShell",
				CustomMinimumSize = new Vector2(150f, 0f),
				LayoutMode = 2,
			};

			var actionMargin = new MarginContainer
			{
				LayoutMode = 2,
			};
			actionMargin.AddThemeConstantOverride("margin_left", 16);
			actionMargin.AddThemeConstantOverride("margin_top", 12);
			actionMargin.AddThemeConstantOverride("margin_right", 16);
			actionMargin.AddThemeConstantOverride("margin_bottom", 12);

			var rightColumn = new VBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			rightColumn.AddThemeConstantOverride("separation", 6);

			var achievementLabel = new Label
			{
				Name = "AchievementCaption",
				LayoutMode = 2,
				Text = "ACHIEVEMENTS",
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			achievementLabel.AddThemeColorOverride("font_color", new Color(0.82f, 0.78f, 0.94f, 0.80f));
			achievementLabel.AddThemeFontSizeOverride("font_size", 11);

			var achievements = new Label
			{
				Name = "AchievementValue",
				LayoutMode = 2,
				Text = BuildAchievementDisplay(_games[i]),
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			achievements.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 1f, 0.98f));
			achievements.AddThemeFontSizeOverride("font_size", 24);

			var actionHint = new Label
			{
				Name = "ActionHint",
				LayoutMode = 2,
				Text = BuildActionHint(i == _selectedIndex),
				HorizontalAlignment = HorizontalAlignment.Right,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			actionHint.AddThemeColorOverride("font_color", new Color(0.72f, 0.90f, 1f, 0.88f));
			actionHint.AddThemeFontSizeOverride("font_size", 13);

			posterCenter.AddChild(posterArt);
			posterCenter.AddChild(posterMonogram);
			posterMargin.AddChild(posterCenter);
			posterShell.AddChild(posterMargin);

			titleRow.AddChild(title);
			if (statusChip != null)
				titleRow.AddChild(statusChip);
			textColumn.AddChild(titleRow);
			if (!string.IsNullOrWhiteSpace(playtime.Text))
			{
				footerRow.AddChild(playtime);
				textColumn.AddChild(footerRow);
			}
			rightColumn.AddChild(achievementLabel);
			rightColumn.AddChild(achievements);
			rightColumn.AddChild(actionHint);
			actionMargin.AddChild(rightColumn);
			actionShell.AddChild(actionMargin);
			split.AddChild(stripe);
			split.AddChild(posterShell);
			split.AddChild(textColumn);
			split.AddChild(actionShell);
			margin.AddChild(split);
			row.AddChild(margin);
			_listRows.AddChild(row);
			_browseEntries.Add(row);
		}
	}

	private void BuildGridTiles()
	{
		if (GetActualGameCount() == 0)
		{
			_gridRows.Columns = 1;
			BuildGridEmptyState();
			return;
		}

		_gridRows.Columns = GridColumns;
		for (int i = 0; i < _games.Count; i++)
		{
			var tile = CreateSelectableEntry(i, gridStyle: true);
			tile.CustomMinimumSize = new Vector2(224f, 166f);
			tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			var margin = new MarginContainer
			{
				Name = "TileMargin",
				LayoutMode = 2,
			};
			margin.AddThemeConstantOverride("margin_left", 14);
			margin.AddThemeConstantOverride("margin_top", 12);
			margin.AddThemeConstantOverride("margin_right", 14);
			margin.AddThemeConstantOverride("margin_bottom", 12);

			var stack = new VBoxContainer
			{
				Name = "TileStack",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			};
			stack.AddThemeConstantOverride("separation", 8);

			var topRow = new HBoxContainer
			{
				Name = "TopRow",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			topRow.AddThemeConstantOverride("separation", 8);

			var channelTag = new Label
			{
				Name = "ChannelTag",
				LayoutMode = 2,
				Text = $"CHANNEL {i + 1:00}",
				HorizontalAlignment = HorizontalAlignment.Left,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			channelTag.AddThemeColorOverride("font_color", new Color(0.78f, 0.74f, 0.92f, 0.82f));
			channelTag.AddThemeFontSizeOverride("font_size", 11);

			var statusBadge = BuildGameBadge(_games[i]);
			PanelContainer? readyTag = string.IsNullOrWhiteSpace(statusBadge)
				? null
				: CreateChip(statusBadge, new Color(0.21f, 0.29f, 0.36f, 0.90f), "GridStateChip", 10);

			var screenShell = new PanelContainer
			{
				Name = "ScreenShell",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0f, 94f),
			};

			var screenMargin = new MarginContainer
			{
				LayoutMode = 2,
			};
			screenMargin.AddThemeConstantOverride("margin_left", 8);
			screenMargin.AddThemeConstantOverride("margin_top", 7);
			screenMargin.AddThemeConstantOverride("margin_right", 8);
			screenMargin.AddThemeConstantOverride("margin_bottom", 7);

			var titleWrap = new CenterContainer
			{
				Name = "ScreenWrap",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			};

			var screenStack = new VBoxContainer
			{
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			screenStack.AddThemeConstantOverride("separation", 3);

			var gridArt = new TextureRect
			{
				Name = "GridArt",
				LayoutMode = 2,
				CustomMinimumSize = new Vector2(0f, 76f),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			};

			var monogram = new Label
			{
				Name = "GridMonogram",
				LayoutMode = 2,
				Text = BuildGameMonogram(_games[i]),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			monogram.AddThemeColorOverride("font_color", new Color(0.75f, 0.88f, 1f, 0.92f));
			monogram.AddThemeFontSizeOverride("font_size", 30);
			BindCoverArt(gridArt, monogram, _games[i]);

			var title = new Label
			{
				Name = "GridTitle",
				LayoutMode = 2,
				Text = _games[i].Title,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			title.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 1f, 0.98f));
			title.AddThemeFontSizeOverride("font_size", 17);

			var projection = new ColorRect
			{
				Name = "ProjectionBar",
				CustomMinimumSize = new Vector2(0f, 5f),
				LayoutMode = 2,
				Color = new Color(0.50f, 0.34f, 0.80f, 0.68f),
			};

			var footerRow = new HBoxContainer
			{
				Name = "FooterRow",
				LayoutMode = 2,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};

			var footerLeft = new Label
			{
				Name = "FooterLeft",
				LayoutMode = 2,
				Text = BuildFormatLabel(_games[i]),
				HorizontalAlignment = HorizontalAlignment.Left,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			footerLeft.AddThemeColorOverride("font_color", new Color(0.76f, 0.78f, 0.90f, 0.82f));
			footerLeft.AddThemeFontSizeOverride("font_size", 11);

			var footerRight = new Label
			{
				Name = "FooterRight",
				LayoutMode = 2,
				Text = BuildAchievementDisplay(_games[i]),
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			footerRight.AddThemeColorOverride("font_color", new Color(0.82f, 0.90f, 1f, 0.90f));
			footerRight.AddThemeFontSizeOverride("font_size", 11);

			topRow.AddChild(channelTag);
			if (readyTag != null)
				topRow.AddChild(readyTag);
			screenStack.AddChild(gridArt);
			screenStack.AddChild(monogram);
			screenStack.AddChild(title);
			titleWrap.AddChild(screenStack);
			screenMargin.AddChild(titleWrap);
			screenShell.AddChild(screenMargin);
			footerRow.AddChild(footerLeft);
			footerRow.AddChild(footerRight);
			stack.AddChild(topRow);
			stack.AddChild(screenShell);
			stack.AddChild(projection);
			stack.AddChild(footerRow);
			margin.AddChild(stack);
			tile.AddChild(margin);
			_gridRows.AddChild(tile);
			_browseEntries.Add(tile);
		}
	}

	private void BuildListEmptyState()
	{
		var row = CreateSelectableEntry(0, gridStyle: false);
		row.CustomMinimumSize = new Vector2(0f, 320f);
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var center = new CenterContainer
		{
			LayoutMode = 2,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};

		var card = CreateEmptyStateCard("Library empty", "Add ROMs to this platform folder or fix the library path, then try again!");
		center.AddChild(card);
		row.AddChild(center);
		_listRows.AddChild(row);
		_browseEntries.Add(row);
	}

	private void BuildGridEmptyState()
	{
		var tile = CreateSelectableEntry(0, gridStyle: true);
		tile.CustomMinimumSize = new Vector2(0f, 340f);
		tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var center = new CenterContainer
		{
			LayoutMode = 2,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};

		var card = CreateEmptyStateCard("No channels yet", "Drop games into the selected platform library and the channel grid will populate here.");
		center.AddChild(card);
		tile.AddChild(center);
		_gridRows.AddChild(tile);
		_browseEntries.Add(tile);
	}

	private PanelContainer CreateEmptyStateCard(string title, string body)
	{
		var card = new PanelContainer
		{
			Name = "EmptyStateCard",
			LayoutMode = 2,
			CustomMinimumSize = new Vector2(640f, 210f),
		};
		card.AddThemeStyleboxOverride("panel", CreatePanelStyleBox(
			new Color(0.11f, 0.10f, 0.19f, 0.96f),
			new Color(0.48f, 0.39f, 0.70f, 0.64f),
			borderWidth: 2,
			radius: 24,
			contentMarginLeft: 28f,
			contentMarginTop: 22f,
			contentMarginRight: 28f,
			contentMarginBottom: 22f));

		var column = new VBoxContainer
		{
			LayoutMode = 2,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		column.AddThemeConstantOverride("separation", 10);

		var eyebrow = new Label
		{
			LayoutMode = 2,
			Text = "PGEMU LIBRARY",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		eyebrow.AddThemeColorOverride("font_color", new Color(0.74f, 0.92f, 1f, 0.88f));
		eyebrow.AddThemeFontSizeOverride("font_size", 12);

		var titleLabel = new Label
		{
			LayoutMode = 2,
			Text = title,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		titleLabel.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 1f, 0.99f));
		titleLabel.AddThemeFontSizeOverride("font_size", 30);

		var bodyLabel = new Label
		{
			LayoutMode = 2,
			Text = body,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		bodyLabel.AddThemeColorOverride("font_color", new Color(0.84f, 0.85f, 0.95f, 0.88f));
		bodyLabel.AddThemeFontSizeOverride("font_size", 15);

		var hintChip = CreateChip(_platform?.Name ?? "Platform", new Color(0.19f, 0.28f, 0.38f, 0.92f), "EmptyStateChip", 11);

		var actionRow = new HBoxContainer
		{
			LayoutMode = 2,
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		actionRow.AddThemeConstantOverride("separation", 12);

		var settingsButton = new Button
		{
			Text = "Library Settings",
			CustomMinimumSize = new Vector2(180f, 42f),
			LayoutMode = 2,
		};
			UiStyle.StyleTopBarButton(settingsButton);
			settingsButton.Pressed += () =>
			{
				OpenVault();
			};
		actionRow.AddChild(settingsButton);

		column.AddChild(eyebrow);
		column.AddChild(titleLabel);
		column.AddChild(bodyLabel);
		column.AddChild(hintChip);
		column.AddChild(actionRow);
		card.AddChild(column);
		return card;
	}

	private PanelContainer CreateSelectableEntry(int index, bool gridStyle)
	{
		var entry = new PanelContainer
		{
			LayoutMode = 2,
			MouseFilter = Control.MouseFilterEnum.Stop,
			FocusMode = Control.FocusModeEnum.All,
		};

		entry.GuiInput += e =>
		{
			if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
			{
				SetSelectedIndex(index);
				if (mb.DoubleClick)
					PlaySelected();
				MarkInputHandled();
			}
		};

		entry.MouseEntered += () =>
		{
			if (_browseLayout == BrowseLayoutMode.Carousel)
				return;
			SetSelectedIndex(index, ensureVisible: false);
		};

		ApplyBrowseEntryStyle(entry, index == _selectedIndex, gridStyle);
		return entry;
	}

	private void ApplyBrowseLayoutMode()
	{
		var showCarousel = _browseLayout == BrowseLayoutMode.Carousel;
		_cardsRoot.Visible = showCarousel;
		_listShell.Visible = _browseLayout == BrowseLayoutMode.List;
		_gridShell.Visible = _browseLayout == BrowseLayoutMode.Grid;
		if (_carousel3D != null)
			_carousel3D.Visible = _browseLayout == BrowseLayoutMode.ThreeD;
		ApplySelectionToBrowseEntries();
		UpdateNavEnabled();
	}

	private void SetSelectedIndex(int index, bool ensureVisible = true)
	{
		if (Count == 0)
			return;

		_selectedIndex = Mathf.Clamp(index, 0, Count - 1);

		if (_browseLayout == BrowseLayoutMode.Carousel)
		{
			_carouselPos = WrapPos(_selectedIndex);
			LayoutCards();
		}

		UpdateSelectionUI();

		if (ensureVisible && _browseLayout != BrowseLayoutMode.Carousel)
			CallDeferred(nameof(EnsureSelectedEntryVisible));
	}

	private void EnsureSelectedEntryVisible()
	{
		if (_browseLayout == BrowseLayoutMode.Carousel)
			return;
		if (_selectedIndex < 0 || _selectedIndex >= _browseEntries.Count)
			return;

		var entry = _browseEntries[_selectedIndex];
		var scroll = _browseLayout == BrowseLayoutMode.List ? _listScroll : _gridScroll;
		var entryTop = (int)entry.Position.Y;
		var entryBottom = entryTop + (int)entry.Size.Y;
		var viewportTop = scroll.ScrollVertical;
		var viewportBottom = viewportTop + (int)scroll.Size.Y;

		if (entryTop < viewportTop)
			scroll.ScrollVertical = entryTop;
		else if (entryBottom > viewportBottom)
			scroll.ScrollVertical = Mathf.Max(0, entryBottom - (int)scroll.Size.Y);
	}

	private void ApplySelectionToBrowseEntries()
	{
		for (int i = 0; i < _browseEntries.Count; i++)
		{
			if (_browseEntries[i] is PanelContainer panel)
			{
				var selected = i == _selectedIndex;
				var gridStyle = _browseLayout == BrowseLayoutMode.Grid;
				ApplyBrowseEntryStyle(panel, selected, gridStyle);
				if (gridStyle)
					ApplyGridEntryContentStyle(panel, _games[i], i, selected);
				else
					ApplyListEntryContentStyle(panel, _games[i], i, selected);
			}
		}
	}

	private static void ApplyBrowseEntryStyle(PanelContainer panel, bool selected, bool gridStyle)
	{
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyleBox(
			selected
				? (gridStyle
					? new Color(0.18f, 0.15f, 0.29f, 0.98f)
					: new Color(0.12f, 0.11f, 0.20f, 0.98f))
				: (gridStyle
					? new Color(0.11f, 0.10f, 0.19f, 0.96f)
					: new Color(0.08f, 0.07f, 0.13f, 0.96f)),
			selected
				? new Color(0.67f, 0.92f, 1f, 1f)
				: new Color(gridStyle ? 0.49f : 0.31f, gridStyle ? 0.40f : 0.28f, gridStyle ? 0.71f : 0.44f, 0.72f),
			borderWidth: selected ? 3 : 2,
			radius: gridStyle ? 26 : 22));
	}

	private void ApplyListEntryContentStyle(PanelContainer panel, GameEntry game, int index, bool selected)
	{
		if (panel.FindChild("AccentBar", true, false) is ColorRect accentBar)
		{
			accentBar.Color = selected
				? new Color(0.58f, 0.92f, 1f, 1f)
				: new Color(0.46f, 0.32f, 0.72f, 0.58f);
		}

		if (panel.FindChild("PosterShell", true, false) is PanelContainer posterShell)
		{
			posterShell.AddThemeStyleboxOverride("panel", CreatePanelStyleBox(
				selected ? new Color(0.18f, 0.17f, 0.31f, 0.98f) : new Color(0.12f, 0.12f, 0.22f, 0.94f),
				selected ? new Color(0.68f, 0.90f, 1f, 0.94f) : new Color(0.48f, 0.39f, 0.70f, 0.58f),
				borderWidth: 2,
				radius: 22));
		}

		if (panel.FindChild("ActionShell", true, false) is PanelContainer actionShell)
		{
			actionShell.AddThemeStyleboxOverride("panel", CreatePanelStyleBox(
				selected ? new Color(0.17f, 0.16f, 0.30f, 0.98f) : new Color(0.10f, 0.10f, 0.19f, 0.94f),
				selected ? new Color(0.58f, 0.90f, 1f, 0.88f) : new Color(0.38f, 0.34f, 0.58f, 0.55f),
				borderWidth: 2,
				radius: 20));
		}

		if (panel.FindChild("PosterSlot", true, false) is Label posterSlot)
			posterSlot.AddThemeColorOverride("font_color", selected
				? new Color(0.88f, 0.94f, 1f, 0.92f)
				: new Color(0.82f, 0.78f, 0.95f, 0.82f));

		if (panel.FindChild("PosterFormat", true, false) is Label posterFormat)
			posterFormat.AddThemeColorOverride("font_color", selected
				? new Color(0.72f, 0.94f, 1f, 0.96f)
				: new Color(0.71f, 0.90f, 1f, 0.88f));

		if (panel.FindChild("PosterMonogram", true, false) is Label posterMonogram)
		{
			posterMonogram.AddThemeColorOverride("font_color", selected
				? new Color(0.74f, 0.92f, 1f, 0.98f)
				: new Color(0.94f, 0.92f, 1f, 0.96f));
			posterMonogram.AddThemeFontSizeOverride("font_size", selected ? 30 : 28);
		}

		if (panel.FindChild("PosterArt", true, false) is TextureRect posterArt)
			posterArt.Modulate = selected ? Colors.White : new Color(1f, 1f, 1f, 0.94f);

		if (panel.FindChild("TitleLabel", true, false) is Label titleLabel)
			titleLabel.AddThemeColorOverride("font_color", selected
				? new Color(1f, 0.99f, 1f, 0.99f)
				: new Color(0.96f, 0.94f, 1f, 0.98f));

		if (panel.FindChild("PlaytimeLabel", true, false) is Label playtimeLabel)
		{
			playtimeLabel.Text = BuildPlaytimeSummary(game);
			playtimeLabel.Visible = !string.IsNullOrWhiteSpace(playtimeLabel.Text);
			playtimeLabel.AddThemeColorOverride("font_color", selected
				? new Color(0.74f, 0.92f, 1f, 0.94f)
				: new Color(0.72f, 0.90f, 1f, 0.88f));
		}

		if (panel.FindChild("AchievementValue", true, false) is Label achievementValue)
		{
			achievementValue.Text = BuildAchievementDisplay(game);
			achievementValue.AddThemeColorOverride("font_color", selected
				? new Color(0.98f, 0.98f, 1f, 1f)
				: new Color(0.92f, 0.88f, 1f, 0.98f));
		}

		if (panel.FindChild("ActionHint", true, false) is Label actionHint)
		{
			actionHint.Text = BuildActionHint(selected);
			actionHint.AddThemeColorOverride("font_color", selected
				? new Color(0.74f, 0.92f, 1f, 0.96f)
				: new Color(0.72f, 0.90f, 1f, 0.88f));
		}

		if (panel.FindChild("StateChip", true, false) is PanelContainer stateChip)
		{
			StyleChip(
				stateChip,
				selected ? new Color(0.20f, 0.40f, 0.46f, 0.96f) : new Color(0.18f, 0.27f, 0.34f, 0.92f),
				selected ? new Color(0.74f, 0.95f, 1f, 0.92f) : new Color(0.52f, 0.60f, 0.76f, 0.42f),
				new Color(0.95f, 0.96f, 1f, 0.98f),
				11);
		}
	}

	private void ApplyGridEntryContentStyle(PanelContainer panel, GameEntry game, int index, bool selected)
	{
		if (panel.FindChild("ScreenShell", true, false) is PanelContainer screenShell)
		{
			screenShell.AddThemeStyleboxOverride("panel", CreatePanelStyleBox(
				selected ? new Color(0.15f, 0.15f, 0.28f, 0.98f) : new Color(0.10f, 0.10f, 0.18f, 0.96f),
				selected ? new Color(0.69f, 0.92f, 1f, 0.92f) : new Color(0.46f, 0.39f, 0.68f, 0.56f),
				borderWidth: selected ? 2 : 1,
				radius: 18));
		}

		if (panel.FindChild("ChannelTag", true, false) is Label channelTag)
			channelTag.AddThemeColorOverride("font_color", selected
				? new Color(0.88f, 0.94f, 1f, 0.92f)
				: new Color(0.78f, 0.74f, 0.92f, 0.82f));

		if (panel.FindChild("GridMonogram", true, false) is Label gridMonogram)
		{
			gridMonogram.Text = BuildGameMonogram(game);
			gridMonogram.AddThemeColorOverride("font_color", selected
				? new Color(0.72f, 0.93f, 1f, 0.98f)
				: new Color(0.75f, 0.88f, 1f, 0.92f));
			gridMonogram.AddThemeFontSizeOverride("font_size", selected ? 32 : 30);
		}

		if (panel.FindChild("GridArt", true, false) is TextureRect gridArt)
			gridArt.Modulate = selected ? Colors.White : new Color(1f, 1f, 1f, 0.94f);

		if (panel.FindChild("GridTitle", true, false) is Label gridTitle)
			gridTitle.AddThemeColorOverride("font_color", selected
				? new Color(1f, 0.99f, 1f, 0.99f)
				: new Color(0.97f, 0.95f, 1f, 0.98f));

		if (panel.FindChild("ProjectionBar", true, false) is ColorRect projectionBar)
		{
			projectionBar.Color = selected
				? new Color(0.64f, 0.92f, 1f, 0.95f)
				: new Color(0.50f, 0.34f, 0.80f, 0.68f);
		}

		if (panel.FindChild("FooterLeft", true, false) is Label footerLeft)
		{
			footerLeft.Text = BuildFormatLabel(game);
			footerLeft.AddThemeColorOverride("font_color", selected
				? new Color(0.88f, 0.90f, 0.98f, 0.92f)
				: new Color(0.76f, 0.78f, 0.90f, 0.82f));
		}

		if (panel.FindChild("FooterRight", true, false) is Label footerRight)
		{
			footerRight.Text = BuildAchievementDisplay(game);
			footerRight.AddThemeColorOverride("font_color", selected
				? new Color(0.74f, 0.92f, 1f, 0.98f)
				: new Color(0.82f, 0.90f, 1f, 0.90f));
		}

		if (panel.FindChild("GridStateChip", true, false) is PanelContainer stateChip)
		{
			StyleChip(
				stateChip,
				selected ? new Color(0.20f, 0.40f, 0.46f, 0.96f) : new Color(0.18f, 0.27f, 0.34f, 0.92f),
				selected ? new Color(0.74f, 0.95f, 1f, 0.92f) : new Color(0.52f, 0.60f, 0.76f, 0.42f),
				new Color(0.95f, 0.96f, 1f, 0.98f),
				10);
		}
	}

	private void RefreshBrowseSelection()
	{
		if (_browseLayout != BrowseLayoutMode.Carousel)
			ApplySelectionToBrowseEntries();
	}

	private static string? BuildGameBadge(GameEntry game)
	{
		return string.IsNullOrWhiteSpace(game.Path) ? "Missing" : null;
	}

	private static string BuildFormatLabel(GameEntry game)
	{
		if (string.IsNullOrWhiteSpace(game.Path))
			return "ROM";

		var extension = Path.GetExtension(game.Path).TrimStart('.').ToUpperInvariant();
		return string.IsNullOrWhiteSpace(extension) ? "ROM" : extension;
	}

	private static string BuildGameMonogram(GameEntry game)
	{
		var words = game.Title
			.Split(new[] { ' ', '-', '_', ':', '.', ',', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
			.Where(word => word.Length > 0)
			.Take(2)
			.Select(word => char.ToUpperInvariant(word[0]))
			.ToArray();

		if (words.Length == 0)
			return "PG";

		return new string(words);
	}

	private static string BuildPlaytimeSummary(GameEntry game)
	{
		return game.TimePlayed <= 0 ? string.Empty : $"{FormatPlaytime(game.TimePlayed)} played";
	}

	private static string BuildAchievementDisplay(GameEntry game)
	{
		return string.IsNullOrWhiteSpace(game.AchievementNum) ? "Loading..." : game.AchievementNum;
	}

	private static string BuildActionHint(bool selected)
	{
		return selected ? "Press A or double-click" : "Select to preview";
	}

	private static string FormatPlaytime(int totalSeconds)
	{
		if (totalSeconds <= 0)
			return "0m";

		var hours = totalSeconds / 3600;
		var minutes = (totalSeconds % 3600) / 60;

		if (hours <= 0)
			return $"{Math.Max(1, minutes)}m";

		return minutes > 0 ? $"{hours}h {minutes:00}m" : $"{hours}h";
	}

	private void BindCoverArt(TextureRect artRect, Label fallbackLabel, GameEntry game)
	{
		fallbackLabel.Text = BuildGameMonogram(game);
		fallbackLabel.Visible = true;
		artRect.Texture = null;
		artRect.Visible = false;

		if (string.IsNullOrWhiteSpace(game.CoverArtUrl))
			return;

		if (CoverArtCache.TryGetValue(game.CoverArtUrl, out var cachedTexture))
		{
			artRect.Texture = cachedTexture;
			artRect.Visible = true;
			fallbackLabel.Visible = false;
			return;
		}

		artRect.SetMeta("pgemu_cover_art_url", game.CoverArtUrl);
		_ = LoadCoverArtAsync(artRect, fallbackLabel, game.CoverArtUrl);
	}

	private async Task LoadCoverArtAsync(TextureRect artRect, Label fallbackLabel, string coverArtUrl)
	{
		try
		{
			byte[] imageData = await CoverArtClient.GetByteArrayAsync(coverArtUrl);
			GD.Print($"Downloaded cover art for {coverArtUrl}"); 
			GD.Print($"Download complete, this valid? {GodotObject.IsInstanceValid(this)}, artRect valid? {GodotObject.IsInstanceValid(artRect)}");
			
			if (!GodotObject.IsInstanceValid(this) ||
				!GodotObject.IsInstanceValid(artRect) ||
				!GodotObject.IsInstanceValid(fallbackLabel) ||
				!artRect.IsInsideTree())
			{
				return;
			}

			Image coverArt = new Image();
			Error loadError = coverArt.LoadPngFromBuffer(imageData);
			if (loadError != Error.Ok)
				loadError = coverArt.LoadJpgFromBuffer(imageData);

			if (loadError != Error.Ok)
				return;

			ImageTexture texture = ImageTexture.CreateFromImage(coverArt);
			CoverArtCache[coverArtUrl] = texture;
			GD.Print($"_carousel3D null? {_carousel3D == null}, valid? {GodotObject.IsInstanceValid(_carousel3D)}");
			GD.Print($"Cached texture for {coverArtUrl}");
			
			if (_carousel3D != null)
			{
				for (int i = 0; i < _games.Count; i++)
				{
					if (string.Equals(_games[i].CoverArtUrl?.Trim(), coverArtUrl.Trim(), 
							StringComparison.OrdinalIgnoreCase))
					{
						_carousel3D.UpdateCoverArt(i, texture);
					}
				}
			}

			if (!artRect.HasMeta("pgemu_cover_art_url") ||
				artRect.GetMeta("pgemu_cover_art_url").AsString() != coverArtUrl)
			{
				return;
			}

			artRect.Texture = texture;
			artRect.Visible = true;
			fallbackLabel.Visible = false;
			
			
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to load cover art: {ex.Message}");
		}
	}

	private static PanelContainer CreateChip(string text, Color background, string? name = null, int fontSize = 11)
	{
		var chip = new PanelContainer
		{
			LayoutMode = 2,
		};
		if (!string.IsNullOrWhiteSpace(name))
			chip.Name = name;

		var label = new Label
		{
			Name = "Label",
			LayoutMode = 2,
			Text = string.IsNullOrWhiteSpace(text) ? "ROM" : text,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		chip.AddChild(label);
		StyleChip(chip, background, new Color(0.74f, 0.70f, 0.94f, 0.22f), new Color(0.94f, 0.93f, 1f, 0.97f), fontSize);
		return chip;
	}

	private static void StyleChip(PanelContainer chip, Color background, Color border, Color textColor, int fontSize)
	{
		chip.AddThemeStyleboxOverride("panel", CreatePanelStyleBox(
			background,
			border,
			borderWidth: 1,
			radius: 999,
			contentMarginLeft: 10f,
			contentMarginTop: 4f,
			contentMarginRight: 10f,
			contentMarginBottom: 4f));

		if (chip.GetNodeOrNull<Label>("Label") is Label label)
		{
			label.AddThemeColorOverride("font_color", textColor);
			label.AddThemeFontSizeOverride("font_size", fontSize);
		}
	}

	private int GetActualGameCount()
	{
		return _games.Count == 1 && string.IsNullOrWhiteSpace(_games[0].Path) ? 0 : _games.Count;
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

	private static string? SelectPlatformPath(
		string? genericPath,
		string? windowsPath,
		string? macPath,
		string? linuxPath)
	{
		if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(windowsPath))
			return windowsPath;
		if (OperatingSystem.IsMacOS() && !string.IsNullOrWhiteSpace(macPath))
			return macPath;
		if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(linuxPath))
			return linuxPath;
		return string.IsNullOrWhiteSpace(genericPath) ? null : genericPath;
	}

	private static string? ResolveConfigPath(AppConfig cfg, string configuredPath, bool allowDirectory)
	{
		if (string.IsNullOrWhiteSpace(configuredPath))
			return null;

		var path = ExpandHomePath(configuredPath).Replace('/', Path.DirectorySeparatorChar);

		if (IsProbablyAbsolutePath(path))
		{
			if (File.Exists(path) || (allowDirectory && Directory.Exists(path)))
				return path;
		}

		var candidates = new[]
		{
			Path.Combine(AppContext.BaseDirectory, path),
				Path.Combine(System.Environment.CurrentDirectory, path),
			cfg.SourcePath != null ? Path.Combine(Path.GetDirectoryName(cfg.SourcePath)!, path) : null,
			!string.IsNullOrWhiteSpace(cfg.LibraryRoot) ? Path.Combine(ExpandHomePath(cfg.LibraryRoot), path) : null,
		};

		foreach (var candidate in candidates)
		{
			if (string.IsNullOrWhiteSpace(candidate))
				continue;
			if (File.Exists(candidate) || (allowDirectory && Directory.Exists(candidate)))
				return candidate;
		}

		return null;
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
	

	private static bool IsProbablyAbsolutePath(string path)
	{
		if (Path.IsPathRooted(path)) return true;

		if (path.Length >= 3 &&
			char.IsLetter(path[0]) &&
			path[1] == ':' &&
			(path[2] == '\\' || path[2] == '/'))
		{
			return true;
		}

		return path.StartsWith(@"\\", StringComparison.Ordinal);
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
		UiStyle.StyleNavButton(_add);
		UiStyle.StyleStatusLabel(_status);
	}
}
