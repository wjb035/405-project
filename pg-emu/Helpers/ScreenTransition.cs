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
		_fadeRect.Color = new Color(0, 0, 0, 1);
		_fadeRect.Modulate = new Color(1, 1, 1, 0); 
		_fadeRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_mat = new ShaderMaterial();
		_mat.Shader = GD.Load<Shader>("res://ShaderSlop/TransitionEffect.gdshader");
		_mat.SetShaderParameter("display_texture", GD.Load<Texture2D>("res://UI/TransitionTextures/display_mask.png"));
		_mat.SetShaderParameter("luminance_cutoff", 0.0f);
		_fadeRect.Material = _mat;
		_mat.SetShaderParameter("screen_size", GetViewport().GetVisibleRect().Size);
		Layer = 100;
	}
	
	private Texture2D GetMaskForTransition(TransitionType type) => type switch
	{
		TransitionType.Spiral => GD.Load<Texture2D>("res://UI/TransitionTextures/spiralBig.png"),
		TransitionType.Noise  => GD.Load<Texture2D>("res://UI/TransitionTextures/noiseBig.png"),
		TransitionType.Wipe   => GD.Load<Texture2D>("res://UI/TransitionTextures/wipe.png"),
		TransitionType.Radial => GD.Load<Texture2D>("res://UI/TransitionTextures/radialBig.png"),
		_                     => GD.Load<Texture2D>("res://UI/TransitionTextures/fade.png"), 
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
	
	public async Task TransitionOut(float duration = 0.5f, TransitionType type = TransitionType.Spiral, bool invert = false)
	{
		_fadeRect.Modulate = new Color(1, 1, 1, 1);
		_fadeRect.Material = _mat;
		_mat.SetShaderParameter("mask_texture", GetMaskForTransition(type));
		_mat.SetShaderParameter("invert", invert); 

		var tween = CreateTween();
		tween.TweenMethod(Callable.From((float v) =>
			_mat.SetShaderParameter("luminance_cutoff", v)), invert ? 0.0f : 1.0f, invert ? 1.0f : 0.0f, duration);
		await ToSignal(tween, "finished");

	}

	public async Task TransitionIn(float duration = 0.5f, TransitionType type = TransitionType.Spiral, bool invert = false)
	{
		_fadeRect.Modulate = new Color(1, 1, 1, 1);
		_fadeRect.Material = _mat;
		_mat.SetShaderParameter("mask_texture", GetMaskForTransition(type));
		_mat.SetShaderParameter("invert", invert); 
		
		var tween = CreateTween();
		tween.TweenMethod(Callable.From((float v) =>
			_mat.SetShaderParameter("luminance_cutoff", v)), invert ? 1.0f : 0.0f, invert ? 0.0f : 1.0f, duration);
		await ToSignal(tween, "finished");
		
		_fadeRect.Material = null; 
		_fadeRect.Modulate = new Color(1, 1, 1, 0);
	}
	
	public async Task ChangeScene(string path, TransitionType type = TransitionType.Fade, float duration = 0.5f, float holdDuration = 0.15f, bool invert = false)
	{
		if (type == TransitionType.Fade)
		{
			await FadeOut(duration);
			GetTree().ChangeSceneToFile(path);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await FadeIn(duration);
		}
		else
		{
			await TransitionOut(duration, type, invert);
			
			_fadeRect.Material = null;
			_fadeRect.Color = new Color(0, 0, 0, 1);
			_fadeRect.Modulate = new Color(1, 1, 1, 1);
			
			await ToSignal(GetTree().CreateTimer(holdDuration), "timeout");
			
			GetTree().ChangeSceneToFile(path);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			
			_fadeRect.Material = _mat;
			_mat.SetShaderParameter("luminance_cutoff", 0.0f);
			
			await TransitionIn(duration, type, invert);
		}
	}
}
