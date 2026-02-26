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

		_registerButton.Pressed += OnRegisterPressed;
		_back.Pressed += GoBack;
	}


	// Reigster chud
	private async void OnRegisterPressed()
	{
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

		// Go to home screen
		GetTree().ChangeSceneToFile("res://HomeScreen.tscn");
	}

	private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
		
	}
}
