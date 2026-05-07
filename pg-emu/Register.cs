using Godot;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PGEmu.Services;


public partial class Register : Control
{
	[Export] public NodePath BackPath;
	[Export] public NodePath UsernamePath;
	[Export] public NodePath EmailPath;
	[Export] public NodePath PasswordPath;
	[Export] public NodePath ConfirmPasswordPath;
	[Export] public NodePath RegisterButtonPath;
	[Export] public NodePath ErrorLabelPath;

	private LineEdit _username = null!;
	private LineEdit _email = null!;
	private LineEdit _password = null!;
	private LineEdit _confirmPassword = null!;
	private Button _registerButton = null!;
	private Button _back = null!;
	private Label _error = null!;

	
	public override void _Ready()
	{
		_username = GetNode<LineEdit>(UsernamePath);
		_email = GetNode<LineEdit>(EmailPath);
		_password = GetNode<LineEdit>(PasswordPath);
		_confirmPassword = GetNode<LineEdit>(ConfirmPasswordPath);
		_registerButton = GetNode<Button>(RegisterButtonPath);
		_back = GetNode<Button>(BackPath);
		_error = GetNode<Label>(ErrorLabelPath);

		ApplyThemeAesthetic();
		ConfigureInputBehavior();

		_registerButton.Pressed += OnRegisterPressed;
		_back.Pressed += GoBack;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
	}

	private void ApplyThemeAesthetic()
	{
		// Match auth screens to the same launcher aesthetic used by the home screen.
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var title = GetNodeOrNull<Label>("Margin/Root/TopBar/Title");
		if (title != null)
		{
			title.Text = "Create Account";
			UiStyle.StyleTitleLabel(title);
		}

		var hint = GetNodeOrNull<Label>("Margin/Root/Body/Hint");
		if (hint != null)
		{
			hint.Text = "Set up your profile and start playing.";
			UiStyle.StyleMetaLabel(hint);
		}

		UiStyle.StyleTopBarButton(_back);
		UiStyle.StyleLineEdit(_username);
		UiStyle.StyleLineEdit(_email);
		UiStyle.StyleLineEdit(_password);
		UiStyle.StyleLineEdit(_confirmPassword);
		UiStyle.StylePrimaryButton(_registerButton);
		UiStyle.TightenButtonContentPadding(_registerButton, horizontal: 6f, vertical: 2f);

		_registerButton.Text = "Create Account";
		_username.PlaceholderText = "Username";
		_email.PlaceholderText = "Email";
		_password.PlaceholderText = "Password";
		_confirmPassword.PlaceholderText = "Confirm Password";

		var userLabel = GetNodeOrNull<Label>("Margin/Root/Body/GridContainer/UserLabel");
		var emailLabel = GetNodeOrNull<Label>("Margin/Root/Body/GridContainer/EmailLabel");
		var passLabel = GetNodeOrNull<Label>("Margin/Root/Body/GridContainer/PassLabel2");
		var confirmLabel = GetNodeOrNull<Label>("Margin/Root/Body/GridContainer/ConfirmPassLabel");
		UiStyle.StyleMetaLabel(userLabel);
		UiStyle.StyleMetaLabel(emailLabel);
		UiStyle.StyleMetaLabel(passLabel);
		UiStyle.StyleMetaLabel(confirmLabel);
		if (userLabel != null) userLabel.HorizontalAlignment = HorizontalAlignment.Left;
		if (emailLabel != null) emailLabel.HorizontalAlignment = HorizontalAlignment.Left;
		if (passLabel != null) passLabel.HorizontalAlignment = HorizontalAlignment.Left;
		if (confirmLabel != null) confirmLabel.HorizontalAlignment = HorizontalAlignment.Left;

		_error.AddThemeColorOverride("font_color", new Color(1f, 0.53f, 0.62f, 0.95f));
	}

	private void ConfigureInputBehavior()
	{
		_confirmPassword.TextSubmitted += _ => OnRegisterPressed();
	}


	// Reigster chud
	private async void OnRegisterPressed()
	{
		if (_registerButton.Disabled)
			return;

		AudioManager.Instance?.PlaySelect();
		_error.Text = "";

		// Validate input
		if (string.IsNullOrWhiteSpace(_username.Text) ||
		    string.IsNullOrWhiteSpace(_email.Text) ||
		    string.IsNullOrWhiteSpace(_password.Text) ||
		    string.IsNullOrWhiteSpace(_confirmPassword.Text))
		{
			_error.Text = "Please fill in all fields.";
			return;
		}

		if (_password.Text != _confirmPassword.Text)
		{
			_error.Text = "Passwords do not match.";
			return;
		}

		_registerButton.Disabled = true;
		_registerButton.Text = "Registering...";

		// Call AuthService
		bool success = await AuthService.Instance.Register(
			_username.Text,
			_email.Text,
			_password.Text
		);

		if (!success)
		{
			_error.Text = "Registration failed. Username or email might be taken.";
			_registerButton.Disabled = false;
			_registerButton.Text = "Register";
			return;
		}

		// Auto-login after registration
		bool loggedIn = await AuthService.Instance.Login(
			_username.Text,
			_password.Text
		);

		if (!loggedIn)
		{
			_error.Text = "Registration succeeded but login failed.";
			_registerButton.Disabled = false;
			_registerButton.Text = "Register";
			return;
		}
		
		await SetDefaultAvatarAsync();

		// Go to home screen
		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
	}

	private void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
		
	}
	
	private async Task SetDefaultAvatarAsync()
	{
		try
		{
			var defaultAvatarPath = ProjectSettings.GlobalizePath("res://Images/default_avatar.png");
			if (!FileAccess.FileExists(defaultAvatarPath))
			{
				GD.PrintErr("Default avatar not found at res://Images/default_avatar.png");
				return;
			}

			var profileService = new ProfileService();
			await profileService.SetAvatar(defaultAvatarPath);
		}
		catch (Exception e)
		{
			GD.PrintErr($"SetDefaultAvatarAsync failed: {e.Message}");
		}
	}
}
