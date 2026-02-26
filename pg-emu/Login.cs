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

		_loginButton.Pressed += async () => await AttemptLogin();
		_back.Pressed += GoBack;
		_registerButton.Pressed += () =>
		{
			GetTree().ChangeSceneToFile("res://RegisterScreen.tscn");
		};
		
		if (AuthService.Instance.IsLoggedIn())
		{
			GetTree().CallDeferred("change_scene_to_file", "res://HomeScreen.tscn");
		}
		
	}
	
	// Calls the authorization controller
	private async Task AttemptLogin()
	{
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
	
	private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
}
