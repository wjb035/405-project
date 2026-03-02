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
		_player.LoadGameWithCore(request.RomPath, request.CorePath, request.CoreId);
		InputRoutingService.Instance?.UnlockUiInput();
		SetStatus("Running in-app emulation. Press Esc to return.");
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
		_player?.StopGame();
	}

	private void ExitToLibrary()
	{
		_player?.StopGame();
		GetTree().ChangeSceneToFile(_returnScene);
	}

	private void SetStatus(string text)
	{
		_status.Text = text;
	}
}
