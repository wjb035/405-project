using Godot;
using System.Threading.Tasks;
namespace PGEmu.Helpers;


public partial class ScreenTransition : CanvasLayer
{
	private ColorRect _fadeRect = null!;

	public override void _Ready()
	{
		_fadeRect = GetNode<ColorRect>("FadeRect");
		_fadeRect.Modulate = new Color(0, 0, 0, 0);
	}

	public async Task FadeOut(float duration = 0.5f)
	{
		var tween = CreateTween();
		tween.TweenProperty(_fadeRect, "modulate:a", 1.0f, duration);
		await ToSignal(tween, "finished");
	}

	public async Task FadeIn(float duration = 0.5f)
	{
		var tween = CreateTween();
		tween.TweenProperty(_fadeRect, "modulate:a", 0.0f, duration);
		await ToSignal(tween, "finished");
	}
	
	public async Task ChangeScene(string path)
	{
		await FadeOut();
		GetTree().ChangeSceneToFile(path);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await FadeIn();
	}
}
