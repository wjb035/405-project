using Godot;
using System.Threading.Tasks;
using PGEmu.Services;

public partial class ResetPassword : Control
{
	public static string PendingEmail = "";

	[Export] public NodePath BackPath;
	[Export] public NodePath CodePath;
	[Export] public NodePath NewPasswordPath;
	[Export] public NodePath ConfirmPasswordPath;
	[Export] public NodePath SubmitButtonPath;
	[Export] public NodePath ErrorLabelPath;

	private Button _back = null!;
	private LineEdit _code = null!;
	private LineEdit _newPassword = null!;
	private LineEdit _confirmPassword = null!;
	private Button _submitButton = null!;
	private Label _error = null!;
	
	public override void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		_code = GetNode<LineEdit>(CodePath);
		_newPassword = GetNode<LineEdit>(NewPasswordPath);
		_confirmPassword = GetNode<LineEdit>(ConfirmPasswordPath);
		_submitButton = GetNode<Button>(SubmitButtonPath);
		_error = GetNode<Label>(ErrorLabelPath);

		ApplyThemeAesthetic();

		_back.Pressed += GoBack;
		_submitButton.Pressed += async () => await AttemptReset();
		_confirmPassword.TextSubmitted += async _ => await AttemptReset();
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

		var title = GetNodeOrNull<Label>("Margin/Root/TopBar/Title");
		if (title != null)
		{
			title.Text = "Reset Password";
			UiStyle.StyleTitleLabel(title);
		}

		var hint = GetNodeOrNull<Label>("Margin/Root/Body/Hint");
		if (hint != null)
		{
			hint.Text = "Enter the code sent to your email.";
			UiStyle.StyleMetaLabel(hint);
		}

		UiStyle.StyleTopBarButton(_back);
		UiStyle.StyleLineEdit(_code);
		UiStyle.StyleLineEdit(_newPassword);
		UiStyle.StyleLineEdit(_confirmPassword);
		UiStyle.StylePrimaryButton(_submitButton);
		UiStyle.TightenButtonContentPadding(_submitButton, horizontal: 6f, vertical: 2f);

		_code.PlaceholderText = "6-digit code";
		_newPassword.PlaceholderText = "New password";
		_confirmPassword.PlaceholderText = "Confirm new password";
		_submitButton.Text = "Reset Password";

		_error.AddThemeColorOverride("font_color", new Color(1f, 0.53f, 0.62f, 0.95f));
	}

	// Try and reset the password
	private async Task AttemptReset()
	{
		// If the buttons disabkled, a request is already in flight
		if (_submitButton.Disabled)
			return;

		_error.Text = "";

		if (string.IsNullOrEmpty(_code.Text.Trim()))
		{
			_error.Text = "Please enter your reset code";
			return;
		}

		if (_newPassword.Text != _confirmPassword.Text)
		{
			_error.Text = "Passwords do not match";
			return;
		}

		if (_newPassword.Text.Length < 8)
		{
			_error.Text = "Password must be at least 8 characters";
			return;
		}

		_submitButton.Disabled = true;

		var (success, message) = await AuthService.Instance.ResetPassword(
			PendingEmail,
			_code.Text.Trim(),
			_newPassword.Text
		);

		_submitButton.Disabled = false;

		if (!success)
		{
			_error.Text = message;
			return;
		}

		// Clear the pending email now that we're done with it
		PendingEmail = "";
		GetTree().ChangeSceneToFile("res://LoginScreen.tscn");
	}

	private void GoBack()
	{
		GetTree().ChangeSceneToFile("res://LoginScreen.tscn");
	}
}
