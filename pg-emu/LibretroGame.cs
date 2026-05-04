using Godot;
using System.IO;
using PGEmu.Services;

public partial class LibretroGame : Control
{
	[Export] public NodePath VideoPath;
	[Export] public NodePath AudioPath;
	[Export] public NodePath StatusPath;
	[Export] public NodePath BackPath;
	[Export] public NodePath TitlePath;

	private TextureRect _video = null!;
	private AudioStreamPlayer _audio = null!;
	private Label _status = null!;
	private Button _back = null!;
	private Label _title = null!;
	private LibretroPlayer _player = null!;
	private string _returnScene = "res://GameSelect.tscn";
	private readonly Godot.Collections.Array<InputEvent> _originalUiCancelEvents = new();
	private GlobalBackground? _globalBackground;
	private bool _uiCancelRemapped;
	private bool _globalBackgroundSuppressed;
	private bool _wasGlobalBackgroundVisible;

	public override void _Ready()
	{
		_video = GetNode<TextureRect>(VideoPath);
		_audio = GetNode<AudioStreamPlayer>(AudioPath);
		_status = GetNode<Label>(StatusPath);
		_back = GetNode<Button>(BackPath);
		_title = GetNode<Label>(TitlePath);

		ConfigureVideoPresentation();

		_back.Pressed += ExitToLibrary;

		_player = new LibretroPlayer();
		AddChild(_player);
		_player.AttachOutput(_video, _audio);

		if (!InProcessLaunchState.TryConsume(out var request))
		{
			SetStatus("No in-app launch request found.");
			return;
		}

		_returnScene = string.IsNullOrWhiteSpace(request.ReturnScene)
			? "res://GameSelect.tscn"
			: request.ReturnScene;

		if (!File.Exists(request.RomPath))
		{
			SetStatus($"ROM not found: {request.RomPath}");
			return;
		}

		if (!File.Exists(request.CorePath))
		{
			SetStatus($"Core not found: {request.CorePath}");
			return;
		}

		_title.Text = string.IsNullOrWhiteSpace(request.GameTitle) ? "In-App Emulation" : request.GameTitle;
		SetStatus($"Loading core: {Path.GetFileName(request.CorePath)}");
		ConfigureGameplayCancelAction();
		SuppressGlobalBackgroundForCore(request.CoreId);
		_player.LoadGameWithCore(request.RomPath, request.CorePath, request.CoreId);
		InputRoutingService.Instance?.UnlockUiInput();
		SetStatus("Running in-app emulation. Press Esc or Guide to return.");
	}

	private void ConfigureVideoPresentation()
	{
		// Prevent stretching: preserve core aspect ratio and center with black bars.
		_video.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_video.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			ExitToLibrary();
		}
	}

	public override void _ExitTree()
	{
		RestoreGameplayCancelAction();
		RestoreGlobalBackground();
		_player?.StopGame();
	}

	private void ExitToLibrary()
	{
		AudioManager.Instance?.PlayClick();
		_player?.StopGame();
		GetTree().ChangeSceneToFile(_returnScene);
	}

	private void SetStatus(string text)
	{
		_status.Text = text;
	}

	private void SuppressGlobalBackgroundForCore(string coreId)
	{
		if (!IsGbaCore(coreId))
			return;

		_globalBackground = GetNodeOrNull<GlobalBackground>("/root/GlobalBackground");
		if (_globalBackground == null)
			return;

		_wasGlobalBackgroundVisible = _globalBackground.Visible;
		_globalBackground.Visible = false;
		_globalBackgroundSuppressed = true;
	}

	private void RestoreGlobalBackground()
	{
		if (!_globalBackgroundSuppressed || _globalBackground == null || !GodotObject.IsInstanceValid(_globalBackground))
			return;

		_globalBackground.Visible = _wasGlobalBackgroundVisible;
		_globalBackgroundSuppressed = false;
		_globalBackground = null;
	}

	private static bool IsGbaCore(string coreId)
	{
		return string.Equals(coreId, "gba", System.StringComparison.OrdinalIgnoreCase) ||
			string.Equals(coreId, "mgba", System.StringComparison.OrdinalIgnoreCase) ||
			string.Equals(coreId, "vbam", System.StringComparison.OrdinalIgnoreCase);
	}

	private void ConfigureGameplayCancelAction()
	{
		if (_uiCancelRemapped)
			return;

		_originalUiCancelEvents.Clear();
		var existingEvents = new Godot.Collections.Array<InputEvent>(InputMap.ActionGetEvents("ui_cancel"));
		foreach (var existingEvent in existingEvents)
		{
			_originalUiCancelEvents.Add(existingEvent);
			InputMap.ActionEraseEvent("ui_cancel", existingEvent);
		}

		InputMap.ActionAddEvent("ui_cancel", new InputEventKey { Keycode = Key.Escape });
		InputMap.ActionAddEvent("ui_cancel", new InputEventJoypadButton { ButtonIndex = JoyButton.Guide });
		_uiCancelRemapped = true;
	}

	private void RestoreGameplayCancelAction()
	{
		if (!_uiCancelRemapped)
			return;

		var currentEvents = new Godot.Collections.Array<InputEvent>(InputMap.ActionGetEvents("ui_cancel"));
		foreach (var currentEvent in currentEvents)
			InputMap.ActionEraseEvent("ui_cancel", currentEvent);

		foreach (var originalEvent in _originalUiCancelEvents)
			InputMap.ActionAddEvent("ui_cancel", originalEvent);

		_uiCancelRemapped = false;
	}
}
