using Godot;

public partial class AchievementScreen : Control
{
	public override void _Ready()
	{
		Button back = GetNode<Button>("Bg/Margin/Root/TopBar/BtnBack");
		back.Pressed += GoBack;
	}

	private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
}
