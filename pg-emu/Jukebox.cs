using Godot;
using System;
using System.IO;
using PGEmu.app;
using PGEmu.Services;

public partial class Jukebox : Control
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

		VBoxContainer container = GetNode<VBoxContainer>("Margin/Root/Body/ScrollContainer/ButtonContainer");
		GD.Print("hi from after container");

		ApplyThemeAesthetic();

		if (_back != null) _back.Pressed += GoBack;

		for (int i = 0; i < PlatformList.platformList.Count; i++)
		{
			Button btn = new Button();
			var platform = PlatformList.platformList[i];
			btn.Text = platform.Name;
			btn.CustomMinimumSize = new Vector2(300, 80);

			UiStyle.StyleTopBarButton(btn);  
			btn.Pressed += () => Launcher.LaunchEmulator(PlatformList._configuration, platform);
			container.AddChild(btn);
		}

		
		foreach (var p in PlatformList.platformList){
			GD.Print(p.Name);
		}





		
GD.Print("Container children: ", container.GetChildCount());
		// Load existing config (or initialize defaults) and populate the UI.
		
		
		
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

	

	

	
}
