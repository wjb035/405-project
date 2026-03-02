using Godot;
using System;
using PGEmu.Services;
using System.Net.Http;
using System.Net.Http.Headers;


public partial class ProfileSettings : Control
{
	[Export] NodePath UsernameEditPath;
	[Export] NodePath UsernameSaveButtonPath;
	[Export] NodePath BioEditPath;
	[Export] NodePath BioSaveButtonPath;
	[Export] NodePath AvatarEditPath;
	[Export] NodePath AvatarSaveButtonPath;
	
	private LineEdit _usernameEdit;
	private Button _usernameSave;
	private LineEdit _bioEdit;
	private Button _bioSave;
	private LineEdit _avatarEdit;
	private Button _avatarSave;
	private Label _status = null!;
	ProfileService _profileService = new ProfileService();

	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_usernameEdit = GetNode<LineEdit>(UsernameEditPath);
		_usernameSave = GetNode<Button>(UsernameSaveButtonPath);
		_bioEdit = GetNode<LineEdit>(BioEditPath);
		_bioSave = GetNode<Button>(BioSaveButtonPath);
		_avatarEdit = GetNode<LineEdit>(AvatarEditPath);
		_avatarSave = GetNode<Button>(AvatarSaveButtonPath);
		_status = GetNode<Label>("Margin/Root/Status");

		ApplyThemeAesthetic();
	
		_usernameSave.Pressed += ChangeUsername;
		_bioSave.Pressed += ChangeBio;
		_avatarSave.Pressed += ChangeAvatar;

	}
	
	public void ChangeUsername() 
	{
		
		GD.Print(_usernameEdit.Text?.Trim());
		_profileService.SetUsername(_usernameEdit.Text?.Trim());
		_status.Text = "Username updated.";
	}
	
	public void ChangeBio() 
	{
		
		GD.Print(_bioEdit.Text?.Trim());
		_profileService.SetBio(_bioEdit.Text?.Trim());
		_status.Text = "Bio updated.";
	}
	
	public void ChangeAvatar() 
	{
		
		GD.Print(_avatarEdit.Text?.Trim());
		_profileService.SetAvatar(_avatarEdit.Text?.Trim());
		_status.Text = "Avatar URL updated.";
	}

	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var title = GetNodeOrNull<Label>("Margin/Root/Title");
		UiStyle.StyleTitleLabel(title);

		var usernameLabel = GetNodeOrNull<Label>("Margin/Root/ChangeUsernameLabel");
		var bioLabel = GetNodeOrNull<Label>("Margin/Root/ChangeBioLabel3");
		var avatarLabel = GetNodeOrNull<Label>("Margin/Root/ChangeAvatarLabel2");
		UiStyle.StyleMetaLabel(usernameLabel);
		UiStyle.StyleMetaLabel(bioLabel);
		UiStyle.StyleMetaLabel(avatarLabel);

		UiStyle.StyleLineEdit(_usernameEdit);
		UiStyle.StyleLineEdit(_bioEdit);
		UiStyle.StyleLineEdit(_avatarEdit);
		_usernameEdit.PlaceholderText = "New username";
		_bioEdit.PlaceholderText = "New bio";
		_avatarEdit.PlaceholderText = "Avatar image URL";

		UiStyle.StylePrimaryButton(_usernameSave);
		UiStyle.StylePrimaryButton(_bioSave);
		UiStyle.StylePrimaryButton(_avatarSave);
		UiStyle.TightenButtonContentPadding(_usernameSave, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_bioSave, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_avatarSave, horizontal: 6f, vertical: 2f);

		UiStyle.StyleStatusLabel(_status);
		_status.Text = "";
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
