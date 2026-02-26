using Godot;
using PGEmu.Services;
using System.Threading.Tasks;

public partial class WelcomeScreen : Control
{
	[Export] public NodePath ContinueButtonPath;
	[Export] public NodePath WelcomeLabelPath;
	[Export] public NodePath ContinueLabelPath;
	[Export] public NodePath FadeRectPath;
	
	private Button _continueButton = null!;
	private Label _welcomeLabel = null!;
	private Label _continue = null!;
	private ColorRect _fadeRect = null!;
	
	public override void _Ready()
	{
		_continueButton = GetNode<Button>(ContinueButtonPath);
		_welcomeLabel = GetNode<Label>(WelcomeLabelPath);
		_continue = GetNode<Label>(ContinueLabelPath);
		_fadeRect = GetNode<ColorRect>(FadeRectPath);
		
		_continueButton.Pressed += OnContinuePressed;

		// Initially transparent
		_fadeRect.Modulate = new Color(0, 0, 0, 0);
		_welcomeLabel.Modulate = new Color(1, 1, 1, 1);
		_continue.Modulate = new Color(1, 1, 1, 1);

		// Start the welcome lo,gic
		CallDeferred(nameof(StartWelcomeFlow));
	}
	
	// If already logged in, skip to homescreen
	private async void StartWelcomeFlow()
	{
		if (AuthService.Instance.IsLoggedIn())
		{
			_welcomeLabel.Text = $"Welcome back, {AuthService.Instance.Username}!";
			
			await Task.Delay(1000);
			
			await FadeOut();
			GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
		}
		else
		{
			_welcomeLabel.Text = "Welcome to PGEmu!";
		}
	}
	
	private async void OnContinuePressed()
	{
		_continueButton.Disabled = true;
		
		await FadeOut();

		// After fade completes, go to next scene
		var nextScene = AuthService.Instance.IsLoggedIn() 
			? "res://HomeScreen.tscn" 
			: "res://LoginScreen.tscn";

		GetTree().ChangeSceneToFile(nextScene);
	}
	
	private async Task FadeOut()
	{
		var tween = CreateTween();

		// Fade out the label first
		tween.TweenProperty(_welcomeLabel, "modulate:a", 0.0f, 0.5f)
			.SetEase(Tween.EaseType.InOut);

		await ToSignal(tween, "finished");

		//  Fade in the black overlay
		tween = CreateTween();
		tween.TweenProperty(_fadeRect, "modulate:a", 1.0f, 0.5f)
			.SetEase(Tween.EaseType.InOut);

		await ToSignal(tween, "finished");
	}
}
