using Godot;
using System;

public partial class Settings : Control
{
	[Export] public NodePath BackPath;
	[Export] public NodePath ProfileChoicePath;
	[Export] public NodePath ProfileScreenPath;
	[Export] public NodePath VaultChoicePath;
	[Export] public NodePath VaultScreenPath;
	
	private Control _profileScreen;
	private Control _vaultScreen;
	
	private Button _back;
	private Button _profileChoice;
	private Button _vaultChoice;
	
	
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		_profileScreen = GetNode<Control>(ProfileScreenPath);
		_vaultScreen = GetNode<Control>(VaultScreenPath);
		_profileChoice = GetNode<Button>(ProfileChoicePath);
		_vaultChoice = GetNode<Button>(VaultChoicePath);
		
		_back.Pressed += GoBack;
		_profileChoice.Pressed += ShowProfileScreen;
		_vaultChoice.Pressed += ShowVaultScreen;
		
	
	
	}
	
		private void GoBack()
	{
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
	
	public void ShowProfileScreen()
	{
		_profileScreen.Visible = true;
		_vaultScreen.Visible = false;
	}
	
	public void ShowVaultScreen()
	{
		_profileScreen.Visible = false;
		_vaultScreen.Visible = true;
	}
	

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
