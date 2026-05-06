using System;
using Godot;
using System.Threading.Tasks;
using PGEmu.Helpers;
using PGEmu.Services;

public partial class Login : Control
{
	[Export] public NodePath BackPath;
	[Export] public NodePath UsernamePath;
	[Export] public NodePath PasswordPath;
	[Export] public NodePath LoginButtonPath;
	[Export] public NodePath RegisterButtonPath;
	[Export] public NodePath ErrorLabelPath;
	[Export] public NodePath ForgotButtonPath;
	[Export] public NodePath ForgotPanelPath;
	[Export] public NodePath ForgotEmailPath; 
	[Export] public NodePath ForgotSubmitPath;

	private LineEdit _username = null!;
	private LineEdit _password = null!;
	private Button _loginButton = null!;
	private Button _registerButton = null!;
	private Button _back = null!;
	private Label _error = null!;
	private Button _forgotButton = null!;
	private Control _forgotPanel = null!;
	private LineEdit _forgotEmail = null!;
	private Button _forgotSubmit = null!;

	private ScreenTransition Transition =>
		GetNode<ScreenTransition>("/root/ScreenTransition");


	public override void _Ready()
	{
		_username = GetNode<LineEdit>(UsernamePath);
		_password = GetNode<LineEdit>(PasswordPath);
		_loginButton = GetNode<Button>(LoginButtonPath);
		_registerButton = GetNode<Button>(RegisterButtonPath);
		_back = GetNode<Button>(BackPath);
		_error = GetNode<Label>(ErrorLabelPath);
		_forgotButton = GetNode<Button>(ForgotButtonPath);
		_forgotPanel = GetNode<Control>(ForgotPanelPath);
		_forgotEmail = GetNode<LineEdit>(ForgotEmailPath);
		_forgotSubmit = GetNode<Button>(ForgotSubmitPath);

		_forgotPanel.Visible = false;
		
		ApplyThemeAesthetic();
		ConfigureInputBehavior();
		

		_loginButton.Pressed += async () => await AttemptLogin();
		_back.Pressed += GoBack;
		_registerButton.Pressed += OpenRegister;
		
		_forgotButton.Pressed += ToggleForgotPanel;
		_forgotSubmit.Pressed += async () => await SubmitForgotPassword();

		
		if (AuthService.Instance.IsLoggedIn())
		{
			var chatManager = GetNode<ChatManager>("/root/ChatManager");
			chatManager.Username = _username.Text; 
			chatManager.ConnectToChat();
			GetTree().CallDeferred("change_scene_to_file", "res://HomeScreen.tscn");
		}
		
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
	}

	private void ApplyThemeAesthetic()
	{
		// Match auth screens to the launcher's neon-dark visual language.
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var title = GetNodeOrNull<Label>("Margin/Root/TopBar/Title");
		if (title != null)
		{
			title.Text = "Sign In";
			UiStyle.StyleTitleLabel(title);
		}

		var hint = GetNodeOrNull<Label>("Margin/Root/Body/Hint");
		if (hint != null)
		{
			hint.Text = "Jump back into your library.";
			UiStyle.StyleMetaLabel(hint);
		}

		UiStyle.StyleTopBarButton(_back);
		UiStyle.AddHoverFeedback(_back);
		UiStyle.ApplyParallaxShadow(_back);
		
		UiStyle.StyleLineEdit(_username);
		UiStyle.StyleLineEdit(_password);
		
		UiStyle.StylePrimaryButton(_loginButton);
		UiStyle.AddHoverFeedback(_loginButton);
		UiStyle.ApplyParallaxShadow(_loginButton);
		
		UiStyle.StylePrimaryButton(_registerButton);
		UiStyle.AddHoverFeedback(_registerButton);
		UiStyle.ApplyParallaxShadow(_registerButton);
		
		UiStyle.TightenButtonContentPadding(_loginButton, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_registerButton, horizontal: 6f, vertical: 2f);
		UiStyle.StyleLineEdit(_forgotEmail);
		
		UiStyle.StylePrimaryButton(_forgotSubmit);
		UiStyle.AddHoverFeedback(_forgotSubmit);
		UiStyle.ApplyParallaxShadow(_forgotSubmit);


		_loginButton.Text = "Sign In";
		_registerButton.Text = "Create Account";
		_username.PlaceholderText = "Username";
		_password.PlaceholderText = "Password";
		_forgotEmail.PlaceholderText = "Email address";
		_forgotSubmit.Text = "Send reset code";
		_forgotButton.Text = "Forgot password?";

		var userLabel = GetNodeOrNull<Label>("Margin/Root/Body/GridContainer/UserLabel");
		var passLabel = GetNodeOrNull<Label>("Margin/Root/Body/GridContainer/PassLabel");
		UiStyle.StyleMetaLabel(userLabel);
		UiStyle.StyleMetaLabel(passLabel);
		if (userLabel != null) userLabel.HorizontalAlignment = HorizontalAlignment.Left;
		if (passLabel != null) passLabel.HorizontalAlignment = HorizontalAlignment.Left;

		_error.AddThemeColorOverride("font_color", new Color(1f, 0.53f, 0.62f, 0.95f));

		// Keep auth CTAs visually balanced by matching the wider button width.
		CallDeferred(nameof(NormalizeActionButtonWidths));
	}

	private void ConfigureInputBehavior()
	{
		// Enter on either input submits the form for keyboard users.
		_username.TextSubmitted += async _ => await AttemptLogin();
		_password.TextSubmitted += async _ => await AttemptLogin();
	}

	private void NormalizeActionButtonWidths()
	{
		float targetWidth = Mathf.Max(
			_loginButton.GetCombinedMinimumSize().X,
			_registerButton.GetCombinedMinimumSize().X
		);

		_loginButton.CustomMinimumSize = new Vector2(targetWidth, _loginButton.CustomMinimumSize.Y);
		_registerButton.CustomMinimumSize = new Vector2(targetWidth, _registerButton.CustomMinimumSize.Y);
	}
	
	// Calls the authorization controller
	private async Task AttemptLogin()
	{
		if (_loginButton.Disabled)
			return;

		AudioManager.Instance?.PlaySelect();
		_error.Text = "";
		_loginButton.Disabled = true;

		bool success = await AuthService.Instance
			.Login(_username.Text, _password.Text);

		_loginButton.Disabled = false;

		if (!success)
		{
			_error.Text = string.IsNullOrWhiteSpace(AuthService.Instance.LastErrorMessage)
				? "Invalid username or password"
				: AuthService.Instance.LastErrorMessage;
			return;
		}

		var chatManager = GetNode<ChatManager>("/root/ChatManager");
		chatManager.Username = _username.Text; 
		chatManager.ConnectToChat();
		await Transition.ChangeScene("res://HomeScreen.tscn",  ScreenTransition.TransitionType.Radial, 0.55f, 0.5f, true);
		
	}

	private void OpenRegister()
	{
		AudioManager.Instance?.PlaySelect();
		GetTree().ChangeSceneToFile("res://RegisterScreen.tscn");
	}
	
	
	// Makes the forgot password panel visible
	private void ToggleForgotPanel()
	{
		_forgotPanel.Visible = !_forgotPanel.Visible;

		// Clear any previous state when toggling
		if (_forgotPanel.Visible)
		{
			_forgotEmail.Text = "";
			_error.Text = "";
			_forgotEmail.GrabFocus();
		}
	}
	
	// Forgot password workflow
	private async Task SubmitForgotPassword()
	{
		var email = _forgotEmail.Text.Trim();

		if (string.IsNullOrEmpty(email))
		{
			_error.Text = "Please enter your email address";
			return;
		}

		_forgotSubmit.Disabled = true;
		_error.Text = "";

		bool success = await AuthService.Instance.ForgotPassword(email);

		_forgotSubmit.Disabled = false;

		if (!success)
		{
			_error.Text = string.IsNullOrWhiteSpace(AuthService.Instance.LastErrorMessage)
				? "Something went wrong, please try again"
				: AuthService.Instance.LastErrorMessage;
			return;
		}

		// Pass the email to the reset screen so it doesn't have to be typed again
		ResetPassword.PendingEmail = email;
		GetTree().ChangeSceneToFile("res://ResetPasswordScreen.tscn");
	}

	
	private async void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		if (string.IsNullOrWhiteSpace(returnScene))
		{
			returnScene = AuthService.Instance.IsLoggedIn()
				? "res://HomeScreen.tscn"
				: "res://WelcomeScreen.tscn"; 
		}


		await Transition.ChangeScene(returnScene,  ScreenTransition.TransitionType.Radial, 0.55f, 0.5f);
	}
	
}
