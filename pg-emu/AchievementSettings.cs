using Godot;
using System;
using PGEmu.Services;
using System.Net.Http;
using System.Net.Http.Headers;


public partial class AchievementSettings : Control
{
	[Export] NodePath UsernameEditPath;
	
	[Export] NodePath ApiEditPath;
	[Export] NodePath ApiSaveButtonPath;
	
	private LineEdit _usernameEdit;
	private Button _usernameSave;
	private LineEdit _apiEdit;
	private Button _apiSave;
	private LineEdit _avatarEdit;
	private Button _avatarSave;
	private Label _status = null!;
	ProfileService _profileService = new ProfileService();

	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_usernameEdit = GetNode<LineEdit>(UsernameEditPath);
		
		_apiEdit = GetNode<LineEdit>(ApiEditPath);
		_apiSave = GetNode<Button>(ApiSaveButtonPath);
		
		_status = GetNode<Label>("Margin/Root/Status");

		ApplyThemeAesthetic();
	
		
		_apiSave.Pressed += ChangeApi;
		

	}
	
	
	
	public void ChangeApi() 
	{
		GD.Print("text pressed in achievement settings!");
		if (_usernameEdit.Text?.Trim() =="" && _apiEdit.Text?.Trim() != ""){
			_status.Text = "Username can't be empty!";
		}
		else if (_apiEdit.Text?.Trim() == "" && _usernameEdit.Text?.Trim() != ""){
			_status.Text = "API Key can't be empty!";
		}
		else if (_apiEdit.Text?.Trim() == "" && _usernameEdit.Text?.Trim() ==""){
			_status.Text = "Username and API Key can't be empty!";
		}
		else{
			GD.Print(_usernameEdit.Text?.Trim());
			GD.Print(_apiEdit.Text?.Trim());
		
			_status.Text = "RetroAcheivements Settings updated.";
		}
	}
	

	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var title = GetNodeOrNull<Label>("Margin/Root/Title");
		UiStyle.StyleTitleLabel(title);

		var usernameLabel = GetNodeOrNull<Label>("Margin/Root/ChangeUsernameLabel");
		var apiLabel = GetNodeOrNull<Label>("Margin/Root/ChangeApiLabel3");
		
		UiStyle.StyleMetaLabel(usernameLabel);
		UiStyle.StyleMetaLabel(apiLabel);
		

		UiStyle.StyleLineEdit(_usernameEdit);
		UiStyle.StyleLineEdit(_apiEdit);
		
		_usernameEdit.PlaceholderText = "RetroAchievements username";
		_apiEdit.PlaceholderText = "RetroAchievements API Key";
		

		UiStyle.StylePrimaryButton(_usernameSave);
		UiStyle.StylePrimaryButton(_apiSave);
		
		UiStyle.TightenButtonContentPadding(_usernameSave, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_apiSave, horizontal: 6f, vertical: 2f);
		

		UiStyle.StyleStatusLabel(_status);
		_status.Text = "";
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
