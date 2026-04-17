using Godot;
using System;
using System.Threading.Tasks;
using PGEmu.Services;
using System.Collections.Generic;

public partial class GlobalBackground : CanvasLayer
{
	private ColorRect _rect;
	private ShaderMaterial _mat;
	public bool HasFadedIn = false;
	private Texture2D default2 = GD.Load<Texture2D>("res://ShaderSlop/DarkPurple.tres");
	private Texture2D default1 = GD.Load<Texture2D>("res://ShaderSlop/BluePurple.tres");
	
	private Texture2D currentTex1;
	private Texture2D currentTex2;
	private Texture2D nextTex1;
	private Texture2D nextTex2;

	
	// Transition animation variables
	private bool _isTransitioning = false;
	private float _transitionValue = 0f;
	private float _transitionDuration = 1.5f;

	
	private Dictionary<string, (Texture2D, Texture2D)> screenGradients = new()
	{
		{"WelcomeScreen", (GD.Load<Texture2D>("res://ShaderSlop/BluePurple.tres"), 
			GD.Load<Texture2D>("res://ShaderSlop/DarkPurple.tres"))},
		{"HomeScreen", (GD.Load<Texture2D>("res://ShaderSlop/Home1.tres"),
			GD.Load<Texture2D>("res://ShaderSlop/Home2.tres"))},
		{"GameScreen", (GD.Load<Texture2D>("res://ShaderSlop/Game1.tres"),
			GD.Load<Texture2D>("res://ShaderSlop/Game2.tres"))},
		{"LoginScreen",  (GD.Load<Texture2D>("res://ShaderSlop/Login1.tres"),
			GD.Load<Texture2D>("res://ShaderSlop/Login2.tres"))},
	};
	
	public override void _Ready()
	{
		_rect = GetNodeOrNull<ColorRect>("ColorRect");

		if (_rect == null)
		{
			GD.PrintErr("Missing ColorRect in GlobalBackground");
			return;
		}

		_mat = _rect.Material as ShaderMaterial;
		if (_mat != null)
		{
			// Make a unique copy so changes update visually
			_mat = _mat.Duplicate() as ShaderMaterial;
			_rect.Material = _mat;
		}
		
		// Set defaults for first load
		currentTex1 = default1;
		currentTex2 = default2;
		nextTex1 = default1;
		nextTex2 = default2;
		
		_mat.SetShaderParameter("tex_frg_80", currentTex1);
		_mat.SetShaderParameter("tex_frg_81", currentTex2);
		_mat.SetShaderParameter("tex_frg_82", currentTex1);
		_mat.SetShaderParameter("tex_frg_83", currentTex2);
		_mat.SetShaderParameter("transition2", 0f);
		
		
		PrintShaderState();
		
		//GD.Print("Default gradients applied: DarkPurple & BluePurple");
		AuthService.Instance.SessionExpired += OnSessionExpired;
	}

	public void SetOpacity(float value)
	{
		_mat.SetShaderParameter("alpha", value);
	}

	public float GetOpacity()
	{
		return (float)_mat.GetShaderParameter("alpha");
	}
	
	
	public async Task FadeIn(float duration = 1.2f)
	{
		if (_mat == null) return;

		var tween = CreateTween();
		tween.TweenMethod(
				Callable.From<float>(v => SetOpacity(v)),
				0f,
				1f,
				duration
			).SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Cubic);

		await ToSignal(tween, Tween.SignalName.Finished);
		HasFadedIn = true;
	}
	
	public async Task FadeOut(float duration = 1.2f)
	{
		if (_mat == null) return;

		var tween = CreateTween();
		tween.TweenMethod(
				Callable.From<float>(v => SetOpacity(v)),
				1f,
				0f,
				duration
			).SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Cubic);

		await ToSignal(tween, Tween.SignalName.Finished);
		HasFadedIn = true;
	}

	// Transition to a specific screen using stored gradients
	public void StartTransition(string screenName, float duration = 1.5f)
	{
		if (!screenGradients.ContainsKey(screenName))
		{
			GD.PrintErr($"No gradients found for '{screenName}'");
			return;
		}

		(nextTex1, nextTex2) = screenGradients[screenName];
		
		_mat.SetShaderParameter("tex_frg_82", nextTex1);
		_mat.SetShaderParameter("tex_frg_83", nextTex2);
		
		_transitionDuration = duration;
		_transitionValue = 0f;
		_isTransitioning = true;

		GD.Print($"Transition to '{screenName}' started.");
		PrintShaderState();
	}

	public override void _Process(double delta)
	{
		if (_isTransitioning)
		{
			_transitionValue += (float)(delta / _transitionDuration);

			// Clamp
			if (_transitionValue > 1f) _transitionValue = 1f;

			// Update shader
			_mat.SetShaderParameter("transition2", _transitionValue);

			// Log current state each frame
			string tex80 = currentTex1?.ResourcePath ?? "null";
			string tex81 = currentTex2?.ResourcePath ?? "null";
			string tex82 = nextTex1?.ResourcePath ?? "null";
			string tex83 = nextTex2?.ResourcePath ?? "null";

			var shaderTransition2 = (float)_mat.GetShaderParameter("transition2");

		//    GD.Print($"Transitioning... _transitionValue={_transitionValue:F3} | shader transition2={shaderTransition2:F3}");
		   // GD.Print($"  tex_frg_80 = {tex80}");
		   // GD.Print($"  tex_frg_81 = {tex81}");
			//GD.Print($"  tex_frg_82 = {tex82}");
		  //  GD.Print($"  tex_frg_83 = {tex83}");

			if (_transitionValue >= 1f)
			{
				_isTransitioning = false;

				currentTex1 = nextTex1;
				currentTex2 = nextTex2;
				
				_mat.SetShaderParameter("tex_frg_80", currentTex1);
				_mat.SetShaderParameter("tex_frg_81", currentTex2);
				_mat.SetShaderParameter("tex_frg_82", currentTex1);
				_mat.SetShaderParameter("tex_frg_83", currentTex2);
				
				_transitionValue = 0f;
				_mat.SetShaderParameter("transition2", 0f);
				
				GD.Print("Transition complete.");
				GD.Print($"  final tex_frg_80 = {currentTex1.ResourcePath}");
				GD.Print($"  final tex_frg_81 = {currentTex2.ResourcePath}");
				GD.Print($"  final transition2 = {(float)_mat.GetShaderParameter("transition2"):F3}");
			}
		}
	}
	
	private void PrintShaderState()
	{
		GD.Print("--- Shader State ---");
		GD.Print($"  tex_frg_80 = {currentTex1?.ResourcePath ?? "null"}");
		GD.Print($"  tex_frg_81 = {currentTex2?.ResourcePath ?? "null"}");
		GD.Print($"  tex_frg_82 = {nextTex1?.ResourcePath ?? "null"}");
		GD.Print($"  tex_frg_83 = {nextTex2?.ResourcePath ?? "null"}");
		GD.Print($"  transition2 = {(float)_mat.GetShaderParameter("transition2"):F3}");
		GD.Print("--------------------");
	}

	private void OnSessionExpired()
	{
		StartTransition("LoginScreen");
		GetTree().ChangeSceneToFile("res://scenes/LoginScreen.tscn"); // adjust path to match yours
	}
}
