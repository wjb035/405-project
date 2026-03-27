using System;
using Godot;
using PGEmu.Services;
using System.Threading.Tasks;
using PGEmu.Helpers;

public partial class WelcomeScreen : Control
{
	[Export] public NodePath ContinueButtonPath;
	[Export] public NodePath WelcomeLabelPath;
	[Export] public NodePath ContinueLabelPath;
	[Export] public NodePath FadeRectPath;
	[Export] public NodePath LogoPath;
	[Export] public NodePath ShadowPath;
	
	private Button _continueButton = null!;
	private Label _welcomeLabel = null!;
	private Label _continue = null!;
	private ColorRect _fadeRect = null!;
	private string _defaultContinuePrompt = string.Empty;
	private TextureRect _logo = null!;
	private TextureRect _shadow = null!;
	private GlobalBackground _bg = null!;
	private ScreenTransition Transition =>
		GetNode<ScreenTransition>("/root/ScreenTransition");
	
	private Vector2 _welcomeOriginalPos;
	private float _floatTime = 0f;
	private bool _floatEnabled = false;
	private Vector2 _floatBasePos;
	
	public override void _Ready()
	{
		_continueButton = GetNode<Button>(ContinueButtonPath);
		_welcomeLabel = GetNode<Label>(WelcomeLabelPath);
		_continue = GetNode<Label>(ContinueLabelPath);
		_fadeRect = GetNode<ColorRect>(FadeRectPath);
		_logo = GetNode<TextureRect>(LogoPath);
		_shadow = GetNode<TextureRect>(ShadowPath);
		_defaultContinuePrompt = _continue.Text;
		_bg = GetNode<GlobalBackground>("/root/GlobalBackground");		
		_continueButton.Pressed += OnContinuePressed;
		Input.JoyConnectionChanged += OnJoyConnectionChanged;
		UpdateContinuePrompt();

		
		// background transition
		StartBackgroundTransition();
		
		// Initially transparent and black
		_bg.SetOpacity(0f);
		_fadeRect.Modulate = new Color(0, 0, 0, 1);
		_welcomeLabel.Modulate = new Color(1, 1, 1, 0);
		_continue.Modulate = new Color(1, 1, 1, 0);
		_welcomeOriginalPos = _welcomeLabel.Position;
		
		_logo.Modulate = new Color(1, 1, 1, 0);    
		_shadow.Modulate = new Color(1, 1, 1, 0);    
		
		// Start the welcome logic
		CallDeferred(nameof(StartWelcomeFlow));
	}

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
			bg.StartTransition("WelcomeScreen", 2f);
			GD.Print("Background transition finished!");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Gradient transition failed: {ex.Message}");
		}
		
	}
	
	public override void _ExitTree()
	{
		Input.JoyConnectionChanged -= OnJoyConnectionChanged;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_continueButton.Disabled)
			return;

		// Let keyboard/controller users continue without mouse interaction.
		var keyPressed = @event is InputEventKey key && key.Pressed && !key.Echo;
		var padPressed = @event is InputEventJoypadButton joy && joy.Pressed;
		if (!keyPressed && !padPressed)
			return;

		GetViewport()?.SetInputAsHandled();
		OnContinuePressed();
	}
	
	// If already logged in, skip to homescreen
	private async void StartWelcomeFlow()
	{
		if (AuthService.Instance.IsLoggedIn())
		{
			_welcomeLabel.Text = $"Welcome back, {AuthService.Instance.Username}!";
			UpdateContinuePrompt();

			// await Task.Delay(3000);
			
			// await FadeOut();
			// GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
		}
		else
		{
			_welcomeLabel.Text = "Welcome to PGEmu!";
			UpdateContinuePrompt();
		}
		await FadeIn();
	}

	private void OnJoyConnectionChanged(long device, bool connected)
	{
		UpdateContinuePrompt();
	}

	private void UpdateContinuePrompt()
	{
		// If any controller is connected, surface controller-friendly copy.
		var hasController = (ControllerService.Instance?.HasActiveController ?? false) ||
			Input.GetConnectedJoypads().Count > 0;
		_continue.Text = hasController
			? "press any button to continue"
			: _defaultContinuePrompt;
	}
	
	private async void OnContinuePressed()
	{
		_continueButton.Disabled = true;

		await FadeOut();
		
		// After fade completes, go to next scene
		var nextScene = AuthService.Instance.IsLoggedIn() 
			? "res://HomeScreen.tscn" 
			: "res://LoginScreen.tscn";

		await Transition.ChangeScene(nextScene);
	}
	
	private async Task FadeOut()
	{
		var tween = CreateTween();

		// Fade out the label first
		tween.TweenProperty(_welcomeLabel, "modulate:a", 0.0f, 0.5f)
			.SetEase(Tween.EaseType.InOut);
		tween.SetParallel(true);
		tween.TweenProperty(_continue, "modulate:a", 0.0f, 0.5f)
			.SetEase(Tween.EaseType.InOut);

		await ToSignal(tween, "finished");

		//  Fade in the black overlay
		tween = CreateTween();
		tween.TweenProperty(_fadeRect, "modulate:a", 1.0f, 0.5f)
			.SetEase(Tween.EaseType.InOut);

		await ToSignal(tween, "finished");
	}
	
	private async Task FadeIn()
	{
		if (!_bg.HasFadedIn)
		{
			_bg.SetOpacity(0f);
			
			await Task.WhenAll(
				_bg.FadeIn(1.2f),
				FadeRectOut()
			);
		} else {
			await FadeRectOut();
		}

		var tween = CreateTween().SetParallel(true);
		tween.TweenProperty(_logo, "modulate:a", 1.0f, 2f)
			.SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(_shadow, "modulate:a", 1.0f, 2f)
			.SetEase(Tween.EaseType.InOut);

		await ToSignal(tween, "finished");

		// Text after
		await FadeText();
		
	}
	private async Task FadeRectOut()
	{
		_fadeRect.Modulate = new Color(0, 0, 0, 1);

		var tween = CreateTween();
		tween.TweenProperty(_fadeRect, "modulate:a", 0.0f, 1.5f);

		await ToSignal(tween, "finished");
	}
	
	// Fades text in and starts its animation cycle
	private async Task FadeText()
	{
		var startPos = _welcomeOriginalPos - new Vector2(30f, 0f);
		_welcomeLabel.Position = startPos;
		_welcomeLabel.Modulate = new Color(1, 1, 1, 0);
		
		var tween = _welcomeLabel.CreateTween();
		tween.TweenProperty(_welcomeLabel, "modulate:a", 1.0f, 1f)
			.SetEase(Tween.EaseType.InOut);
		tween.SetParallel(true);
		tween.TweenProperty(_welcomeLabel, "position", _welcomeOriginalPos, 1f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Sine);

		await ToSignal(tween, "finished");
		
		await Task.Delay(100);
		
		_floatBasePos = _welcomeLabel.Position;
		_floatEnabled = true;
		
		await Task.Delay(600);

		_continue.Modulate = new Color(1, 1, 1, 0);

		tween = _continue.CreateTween();
		tween.TweenProperty(_continue, "modulate:a", 1.0f, 1.5f);

		await ToSignal(tween, "finished");
		
	}
	
	// Subtle bob left and right	
	public override void _Process(double delta)
	{
		if (_welcomeLabel == null || !_floatEnabled) return;

		// Amplitude in pixels and period in seconds
		float amplitude = 10f;
		float period = 6f;

		_floatTime += (float)delta;

		// Smooth sine wave motion along X
		float offsetX = amplitude * Mathf.Sin((_floatTime / period) * Mathf.Tau - Mathf.Pi / 2);
		_welcomeLabel.Position = _floatBasePos + new Vector2(offsetX, 0);
	}
}
