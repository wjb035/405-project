using Godot;
using System;

public partial class Profile : Control
{
	[Export] public NodePath BackPath;

	private Button _back = null!;

	public override void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		_back.Pressed += GoBack;
	}

	private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
}

