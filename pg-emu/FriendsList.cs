using Godot;
using System;

public partial class FriendsList : Node
{	
	private Button _back = null!;
	
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_back = GetNode<Button>("Margin/Root/TopBar/Panel/BtnBack");
		_back.Pressed += GoBack;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	
	
		private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
}
