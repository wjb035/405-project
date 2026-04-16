using Godot;
using System.Threading.Tasks;
namespace PGEmu.Helpers;


public partial class ScreenTransition : CanvasLayer
{
	public enum TransitionType { Fade, Spiral, Noise, Wipe, Radial }
	
	private ColorRect _fadeRect = null!;
	private ShaderMaterial _mat = null!;
	
	public override void _Ready()
	{
		_fadeRect = GetNode<ColorRect>("FadeRect");
		_fadeRect.Modulate = new Color(0, 0, 0, 0);
		_mat = new ShaderMaterial();
		_mat.Shader = GD.Load<Shader>("res://ShaderSlop/TransitionEffect.gdshader");
		_mat.SetShaderParameter("luminance_cutoff", 0.0f);
		_fadeRect.Material = _mat;
	}
	
	private Texture2D GetMaskForTransition(TransitionType type) => type switch
	{
		TransitionType.Spiral => GD.Load<Texture2D>("res://UI/TransitionTextures/finalspiral.tres"),
		TransitionType.Noise  => GD.Load<Texture2D>("res://UI/TransitionTextures/noise.tres"),
		TransitionType.Wipe   => GD.Load<Texture2D>("res://UI/TransitionTextures/wipe.tres"),
		TransitionType.Radial => GD.Load<Texture2D>("res://Textures/Transitions/radial.png"),
		_                     => GD.Load<Texture2D>("res://Textures/Transitions/fade.png"), 
	};

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
