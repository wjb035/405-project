using Godot;
using System.Threading.Tasks;
using PGEmu.Services;

public partial class Login : Control
{
	[Export] public NodePath BackPath;
	[Export] public NodePath UsernamePath;
	[Export] public NodePath PasswordPath;
	[Export] public NodePath LoginButtonPath;
	[Export] public NodePath RegisterButtonPath;
	[Export] public NodePath ErrorLabelPath;


	private LineEdit _username = null!;
	private LineEdit _password = null!;
	private Button _loginButton = null!;
	private Button _registerButton = null!;
	private Button _back = null!;
	private Label _error = null!;

	public override void _Ready()
	{
		_username = GetNode<LineEdit>(UsernamePath);
		_password = GetNode<LineEdit>(PasswordPath);
		_loginButton = GetNode<Button>(LoginButtonPath);
		_registerButton = GetNode<Button>(RegisterButtonPath);
		_back = GetNode<Button>(BackPath);
		_error = GetNode<Label>(ErrorLabelPath);

		ApplyThemeAesthetic();
		ConfigureInputBehavior();

		_loginButton.Pressed += async () => await AttemptLogin();
		_back.Pressed += GoBack;
		_registerButton.Pressed += OpenRegister;
		
		if (AuthService.Instance.IsLoggedIn())
		{
			GetTree().CallDeferred("change_scene_to_file", "res://HomeScreen.tscn");
		}
		
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
		UiStyle.StyleLineEdit(_username);
		UiStyle.StyleLineEdit(_password);
		UiStyle.StylePrimaryButton(_loginButton);
		UiStyle.StylePrimaryButton(_registerButton);
		UiStyle.TightenButtonContentPadding(_loginButton, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_registerButton, horizontal: 6f, vertical: 2f);

		_loginButton.Text = "Sign In";
		_registerButton.Text = "Create Account";
		_username.PlaceholderText = "Username";
		_password.PlaceholderText = "Password";

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
			_error.Text = "Invalid username or password";
			return;
		}

		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
	}

	private void OpenRegister()
	{
		AudioManager.Instance?.PlaySelect();
		GetTree().ChangeSceneToFile("res://RegisterScreen.tscn");
	}
	
	private void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
}
