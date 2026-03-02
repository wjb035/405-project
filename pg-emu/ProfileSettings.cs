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
	
		_usernameSave.Pressed += ChangeUsername;
		_bioSave.Pressed += ChangeBio;
		_avatarSave.Pressed += ChangeAvatar;

	}
	
	public void ChangeUsername() 
	{
		
		GD.Print(_usernameEdit.Text?.Trim());
		_profileService.SetUsername(_usernameEdit.Text?.Trim());
	}
	
	public void ChangeBio() 
	{
		
		GD.Print(_bioEdit.Text?.Trim());
		_profileService.SetBio(_bioEdit.Text?.Trim());
	}
	
	public void ChangeAvatar() 
	{
		
		GD.Print(_avatarEdit.Text?.Trim());
		_profileService.SetAvatar(_avatarEdit.Text?.Trim());
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
