using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PGEmu.Services;

public partial class ConsoleCarousel3DView : SubViewportContainer
{
	private const ulong SettleSpinSuppressWindowMs = 40;
	private const string GbaScreenLogoPath = "res://Models/gba_logo2.png";
	private const string PspScreenLogoPath = "res://Models/psp_logo.png";
	private const float PspScreenNudgeLeftU = 0.220f;
	private const float GbaScreenUvScaleU = 3.074675f;
	private const float GbaScreenUvScaleV = -4.6753664f;
	private const float GbaScreenUvOffsetU = -1.825017f;
	private const float GbaScreenUvOffsetV = 3.91563f;
	private const double GbaOpen = 0.85;
	private const double GbaSelectionOpen = 1.2;
	private const double N64SelectionOpen = 1.0;
	private const double GameCubeRestPose = 2.125;
	private const float SelectionAnimationSpeedScale = 1.18f;
	// Physics
	private const float HoverMotionThreshold = 0.001f;
	private float _velocity = 0f;
	private const float Friction = 3.0f;
	private const float DragScale = 0.004f;
	private const float FlingMultiplier = 15f; 
	private const float ClickDragThreshold = 14f;
	private float _dragStartX;
	private float _dragStartPos;
	private Vector2 _dragStartMousePos;
	private bool _dragging;
	private bool _dragMoved;
	private float _lastDragX;
	private float _lastDragVelocity;

	// Carousel state (mirrors GameSelect._carouselPos)
	public float CarouselPos { get; set; } = 0f;
	private int _count = 0;
	private float _spinAudioPos = 0f;
	private float _lastSpinAudioCarouselPos = 0f;
	private int _lastSpinAudioStep = 0;
	private ulong _lastSpinAudioMs;
	
	private float _spinSpeed = 0f;
	private const float SpinSpeedDecay = 3f;
	private const float SpinSpeedMax = 1.0f;

	// 3D scene internals
	private SubViewport _viewport;
	private Node3D _sceneRoot;
	private Camera3D _camera;
	private readonly List<Node3D> _boxes = new();
	
	// Card spacing in 3D units
	private const float Spacing = 2.2f;
	private const float SelectedScale = 1.0f;
	private const float UnselectedScale = 0.72f;

	// Tweening animation
	private int _hoveredIdx = -1;
	private Tween? _hoverTween;
	private double _mouseIdleTime = 0f;
	private const double MouseIdleThreshold = 1.0; 
	
	// Console colors
	private Dictionary<StandardMaterial3D, Color> _originalColors = new();
	private readonly Dictionary<Node3D, AnimationPlayer> _consoleAnimations = new();
	private readonly Dictionary<Node3D, string> _consoleAnimationNames = new();
	private readonly Dictionary<Node3D, double> _consoleAnimationLengths = new();
	private readonly Dictionary<Node3D, double> _consoleAnimationHoldTimes = new();
	private readonly HashSet<Node3D> _selectionAnimatedConsoles = new();
	private readonly HashSet<Node3D> _returnAnimatedConsoles = new();
	private readonly HashSet<Node3D> _openAnimatedConsoles = new();
	private readonly HashSet<Node3D> _manuallyClosedAnimatedConsoles = new();
	private readonly HashSet<Node3D> _pendingConsoleCloseSounds = new();
	private Texture2D? _gbaScreenLogoTexture;
	private Texture2D? _pspScreenLogoTexture;
	
	public event System.Action<int>? SelectionChanged;

	// Types of consoles we support
	public enum ConsoleType { Wii, NintendoDS, Nintendo64, SNES, NES, PlayStation1, PlayStation2, PSP, GameCube, GBA }
	
	public override void _Ready()
	{
		// Builds the SubViewport, which basically renders a 3d sub scene in a 2d UI.
		_viewport = new SubViewport
		{
			Name = "Viewport3D",
			TransparentBg = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(_viewport);
		_viewport.PositionalShadowAtlasSize = 4096;
		_viewport.PositionalShadowAtlas16Bits = true;
		Stretch = true;
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		MouseFilter = MouseFilterEnum.Pass;
		ClipContents = false;
		
		// Scene root
		_sceneRoot = new Node3D { Name = "SceneRoot" };
		_viewport.AddChild(_sceneRoot);

		
		// Camera
		_camera = new Camera3D
		{
			Name = "Camera",
			Position = new Vector3(0, 0.8f, 5f),
			
		};
		_camera.RotateX(Mathf.DegToRad(-8f));
		_camera.Fov = 60f;
		_sceneRoot.AddChild(_camera);
		
		// Lighting
		var sun = new DirectionalLight3D
		{
			LightEnergy = 2.1f,
			LightColor = new Color(1f, 0.95f, 0.85f),
			ShadowEnabled = true,
			ShadowBlur = 0.5f,
		};
		sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
		sun.DirectionalShadowMaxDistance = 20f;
		sun.ShadowBias = 0.2f;
		sun.RotateX(Mathf.DegToRad(-45f));
		sun.RotateY(Mathf.DegToRad(70f));
		_sceneRoot.AddChild(sun);

		var fill = new DirectionalLight3D
		{
			LightEnergy = 0.7f,
			LightColor = new Color(0.45f, 0.38f, 1f),
		};
		fill.RotateX(Mathf.DegToRad(20f));
		fill.RotateY(Mathf.DegToRad(-120f));
		_sceneRoot.AddChild(fill);
		
		var rim = new OmniLight3D
		{
			Position = new Vector3(0, 3f, -4f),
			LightEnergy = 1.0f,
			OmniRange = 15f,
			LightColor = new Color(0.50f, 0.65f, 1.0f), 
		};
		_sceneRoot.AddChild(rim);
		
		var env = new Environment();
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.25f, 0.3f, 0.65f);
		env.AmbientLightEnergy = 0.7f;

		var worldEnv = new WorldEnvironment { Environment = env };
		_sceneRoot.AddChild(worldEnv);
		
		// Ground for recieving shadows
		var ground = new MeshInstance3D();
		ground.Mesh = new PlaneMesh { Size = new Vector2(250f, 50f) };
		// position below the consoles
		ground.Position = new Vector3(-30f, -1.2f, 2f); 
		ground.RotateX(Mathf.DegToRad(-4f));

		var groundMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.08f, 0.05f, 0.25f, 0.5f),
			Roughness = 1f,
			// ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
			// ShadowToOpacity = true,
		};
		   
		ground.SetSurfaceOverrideMaterial(0, groundMat);
		ground.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		ground.MaterialOverlay = null;
		_sceneRoot.AddChild(ground);
		
		// Aliasing
		_viewport.Msaa3D = Viewport.Msaa.Msaa8X;
		_viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
		_viewport.Scaling3DMode = SubViewport.Scaling3DModeEnum.Fsr;
		_viewport.Scaling3DScale = 1.0f; 
		_viewport.FsrSharpness = 0.3f;
		
		//This ends up causing a blank screen so I'm commenting it out for now - Journey
		/* var overlay = new ColorRect();
		overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Ignore; 
		var overlayMat = new ShaderMaterial();
		overlayMat.Shader = GD.Load<Shader>("res://ShaderSlop/PS1Effect.gdshader");
		overlayMat.SetShaderParameter("resolution", new Vector2I(320, 240));
		overlayMat.SetShaderParameter("jitter", 0.25f);
		overlayMat.SetShaderParameter("affine_mapping", true);
		overlay.Material = overlayMat;
		AddChild(overlay); */
	}

	// Call this from GameSelect after loading games to clear everything and rebuild
	public void Populate(List<ConsoleType> consoles, float initialPos = 0f)
	{
		// Clear old boxes
		foreach (var b in _boxes)
			b.QueueFree();
		_boxes.Clear();
		_consoleAnimations.Clear();
		_consoleAnimationNames.Clear();
		_consoleAnimationLengths.Clear();
		_consoleAnimationHoldTimes.Clear();
		_selectionAnimatedConsoles.Clear();
		_returnAnimatedConsoles.Clear();
		_openAnimatedConsoles.Clear();
		_manuallyClosedAnimatedConsoles.Clear();

		
		_count = consoles.Count;
		CarouselPos = WrapPos(initialPos);
		_spinAudioPos = CarouselPos;
		_lastSpinAudioCarouselPos = CarouselPos;
		_lastSpinAudioStep = Mathf.RoundToInt(_spinAudioPos);
		_lastSpinAudioMs = 0;

		for (int i = 0; i < consoles.Count; i++)
		{
			var box = BuildConsole(consoles[i]);
			_sceneRoot.AddChild(box);
			_boxes.Add(box);
		}

		LayoutBoxes();
	}
	
	// Builds actual 3d geometry of the cases
	private Node3D BuildConsole(ConsoleType type)
	{
		var modelPath = type switch
		{
			ConsoleType.Wii          => "res://Models/wii_console.glb",
			ConsoleType.NintendoDS   => "res://Models/ds.glb",
			ConsoleType.Nintendo64   => "res://Models/n64.glb",
			ConsoleType.SNES         => "res://Models/SNES.glb",
			ConsoleType.NES          => "res://Models/NES.glb",
			ConsoleType.PlayStation1 => "res://Models/ps1.glb",
			ConsoleType.PlayStation2 => "res://Models/ps2.glb",
			ConsoleType.PSP          => "res://Models/psp.glb",
			ConsoleType.GameCube     => "res://Models/gamecube.glb",
			ConsoleType.GBA          => "res://Models/gba.glb",
			_                        => null
		};
		
		// GD.Print($"Looking for model at: {modelPath} — exists: {ResourceLoader.Exists(modelPath)}");

		if (modelPath == null || !ResourceLoader.Exists(modelPath))
		{
			GD.PrintErr($"Model not found for {type}, using placeholder");
			return BuildPlaceholder(type); // fall back to box geometry
		}

		var scene = GD.Load<PackedScene>(modelPath);
		var model = scene.Instantiate<Node3D>();
		if (type == ConsoleType.PSP)
		{
			EnsurePspRenderable(model);
			ApplyPspScreenLogo(model);
		}
		else if (type == ConsoleType.GBA)
			ApplyGbaScreenLogo(model);
		EnableShadows(model);
		var wrapper = new Node3D { Name = type.ToString() };
		Node3D modelRoot = model;
	   //  wrapper.RotateX(Mathf.DegToRad(-20f));
			switch (type)
			{
			case ConsoleType.Wii:
				SetWiiDiskVisible(model, false);
				// CenterNode3D(model, ShouldIncludeWiiBodyBounds);
				var wiiScale = ScaleToFit(model, 2.5f, ShouldIncludeWiiBodyBounds);
				model.Scale = new Vector3(wiiScale, wiiScale, wiiScale);
				// model.Position = new Vector3(-0.8f, -.5f, 0f);
				if (TryGetNodeBounds(model, Transform3D.Identity, out var wiiBounds, ShouldIncludeWiiBodyBounds))
				{
					var center = wiiBounds.GetCenter();
					model.Position = new Vector3(-center.X, -center.Y, -center.Z);
				}
				model.RotateY(Mathf.DegToRad(40f));
				break;
			case ConsoleType.NintendoDS:
				CenterNode3D(model);
				var dsScale = ScaleToFit(model, 2.65f);
				model.Scale = new Vector3(dsScale, dsScale, dsScale);
				model.Position = new Vector3(0.05f, -0.58f, 0.35f);
				model.RotateX(Mathf.DegToRad(7f));
				model.RotateY(Mathf.DegToRad(-6f));
				break;
			case ConsoleType.Nintendo64:
				CenterNode3D(model);
				var n64Scale = ScaleToFit(model, 3.2f);
				model.Scale = new Vector3(n64Scale, n64Scale, n64Scale);
				model.Position = new Vector3(1f, -0.5f, 0.1f);
				model.RotateX(Mathf.DegToRad(0f));
				model.RotateY(Mathf.DegToRad(30f));
				SetN64CartridgeVisible(model, false);
				break;
			case ConsoleType.SNES:
				CenterNode3D(model);
				var snesScale = ScaleToFit(model, 2.75f);
				model.Scale = new Vector3(snesScale, snesScale, snesScale);
				model.Position = new Vector3(0f, -0.48f, 0f);
				model.RotateX(Mathf.DegToRad(0f));
				model.RotateY(Mathf.DegToRad(8f));
				SetSnesCartridgeVisible(model, false);
				break;
			case ConsoleType.NES:
				CenterNode3D(model);
				var nesScale = ScaleToFit(model, 7.1f);
				model.Scale = new Vector3(nesScale, nesScale, nesScale);
				model.Position = new Vector3(1f, -0.55f, 0.08f);
				model.RotateX(Mathf.DegToRad(1f));
				model.RotateY(Mathf.DegToRad(108f));
				SetNesCartridgeVisible(model, false);
				break;
			case ConsoleType.PlayStation1:
				SetPs1DiskVisible(model, false);
				CenterNode3D(model);
				var ps1Scale = ScaleToFit(model, 2.75f);
				model.Scale = new Vector3(ps1Scale, ps1Scale, ps1Scale);
				model.Position = new Vector3(0.02f, -0.5f, 0.1f);
				model.RotateX(Mathf.DegToRad(8f));
				model.RotateY(Mathf.DegToRad(10f));
				break;
			case ConsoleType.PlayStation2:
				SetPs2DiskVisible(model, false);
				CenterNode3D(model);
				var ps2Scale = ScaleToFit(model, 3.5f);
				model.Scale = new Vector3(ps2Scale, ps2Scale, ps2Scale);
				model.Position = new Vector3(-0.25f, -1f, .1f);
				model.RotateY(Mathf.DegToRad(100f));
				break;
			case ConsoleType.PSP:
				SetPspDiskVisible(model, false);
				var pspScale = ScaleToFit(model, 4.4f);
				model.Scale = new Vector3(pspScale, pspScale, pspScale);
				CenterNode3D(model);
				var pspPivot = new Node3D { Name = "PSPPivot" };
				pspPivot.Position = new Vector3(0f, 1.25f, 0f);
				pspPivot.RotateX(Mathf.DegToRad(10f));
				pspPivot.RotateY(Mathf.DegToRad(-170f));
				pspPivot.AddChild(model);
				modelRoot = pspPivot;
				break;
			case ConsoleType.GameCube:
				model.Scale = new Vector3(0.030f, 0.030f, 0.030f);
				model.Position = new Vector3(0, -0.7f, 0);
				model.RotateY(Mathf.DegToRad(30f));
				SetGameCubeDiskVisible(model, false);
				break;
			case ConsoleType.GBA:
				CenterNode3D(model);
				model.Scale = new Vector3(0.15f, 0.15f, 0.15f);
				model.Position = new Vector3(0.08f, -0.22f, 0f);
				model.RotateX(Mathf.DegToRad(14f));
				model.RotateY(Mathf.DegToRad(-18f));
				SetGbaCartridgeVisible(model, false);
				break;
			}
		wrapper.AddChild(modelRoot);
		ConfigureConsoleAnimation(wrapper, model, type);
		return wrapper;
		
	}
	
	//  Fallback for if the console doesnt have a model
	private Node3D BuildPlaceholder(ConsoleType type)
{
	var root = new Node3D { Name = type.ToString() };
	var mesh = new MeshInstance3D { Name = "Mesh" };

	switch (type)
	{
		case ConsoleType.Wii:
			mesh.Mesh = new BoxMesh { Size = new Vector3(0.8f, 3.2f, 0.4f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.92f, 0.92f, 0.90f)));
			break;
		case ConsoleType.NintendoDS:
			mesh.Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.38f, 1.9f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.80f, 0.82f, 0.90f)));
			break;
		case ConsoleType.Nintendo64:
			mesh.Mesh = new BoxMesh { Size = new Vector3(2.5f, 0.55f, 1.8f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.12f, 0.12f, 0.15f)));
			var cartridgeSlot = new MeshInstance3D();
			cartridgeSlot.Mesh = new BoxMesh { Size = new Vector3(1.0f, 0.10f, 0.32f) };
			cartridgeSlot.Position = new Vector3(0f, 0.32f, -0.18f);
			cartridgeSlot.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.04f, 0.04f, 0.05f), metallic: 0.5f));
			root.AddChild(cartridgeSlot);
			break;
		case ConsoleType.SNES:
			mesh.Mesh = new BoxMesh { Size = new Vector3(2.7f, 0.55f, 1.9f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.62f, 0.60f, 0.67f)));
			break;
		case ConsoleType.NES:
			mesh.Mesh = new BoxMesh { Size = new Vector3(2.8f, 0.5f, 1.8f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.55f, 0.55f, 0.58f)));
			break;
		case ConsoleType.PlayStation1:
			mesh.Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.45f, 1.8f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.63f, 0.63f, 0.60f)));
			var discLid = new MeshInstance3D();
			discLid.Mesh = new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 0.04f };
			discLid.Position = new Vector3(-0.3f, 0.25f, 0.05f);
			discLid.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.48f, 0.48f, 0.46f)));
			root.AddChild(discLid);
			var buttons = new MeshInstance3D();
			buttons.Mesh = new BoxMesh { Size = new Vector3(0.65f, 0.06f, 0.18f) };
			buttons.Position = new Vector3(0.68f, 0.27f, -0.48f);
			buttons.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.24f, 0.24f, 0.24f)));
			root.AddChild(buttons);
			break;
		case ConsoleType.PlayStation2:
			mesh.Mesh = new BoxMesh { Size = new Vector3(1.2f, 3.0f, 0.7f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.05f, 0.05f, 0.08f)));
			break;
		case ConsoleType.PSP:
			mesh.Mesh = new BoxMesh { Size = new Vector3(3.2f, 1.5f, 0.3f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.12f, 0.12f, 0.16f)));
			var screen = new MeshInstance3D();
			screen.Mesh = new QuadMesh { Size = new Vector2(1.8f, 1.1f) };
			screen.Position = new Vector3(-0.4f, 0.1f, 0.16f);
			screen.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.05f, 0.08f, 0.15f), metallic: 0.8f));
			root.AddChild(screen);
			break;
		case ConsoleType.GameCube:
			mesh.Mesh = new BoxMesh { Size = new Vector3(2.2f, 2.2f, 2.2f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.25f, 0.22f, 0.35f)));
			var lid = new MeshInstance3D();
			lid.Mesh = new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.7f, Height = 0.05f };
			lid.Position = new Vector3(0.2f, 0.6f, 1.12f);
			lid.RotateX(Mathf.DegToRad(90f));
			lid.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.18f, 0.16f, 0.26f)));
			root.AddChild(lid);
			break;
		case ConsoleType.GBA:
			mesh.Mesh = new BoxMesh { Size = new Vector3(2.8f, 1.4f, 0.25f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.55f, 0.50f, 0.70f)));
			var gbaScreen = new MeshInstance3D();
			gbaScreen.Mesh = new QuadMesh { Size = new Vector2(1.2f, 0.9f) };
			gbaScreen.Position = new Vector3(0f, 0.1f, 0.13f);
			gbaScreen.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.05f, 0.08f, 0.15f), metallic: 0.8f));
			root.AddChild(gbaScreen);
			break;
	}

	root.AddChild(mesh);
	return root;
}

	
	private static StandardMaterial3D MakeMat(Color color, float roughness = 0.5f, float metallic = 0.2f) =>
		new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, };

	private static void CenterNode3D(Node3D root, System.Func<Node3D, bool>? includeNode = null)
	{
		if (!TryGetNodeBounds(root, Transform3D.Identity, out var bounds, includeNode))
			return;

		root.Position -= bounds.GetCenter();
	}

	private static float ScaleToFit(Node3D root, float targetMaxDimension, System.Func<Node3D, bool>? includeNode = null)
	{
		if (targetMaxDimension <= 0f ||
			!TryGetNodeBounds(root, Transform3D.Identity, out var bounds, includeNode))
		{
			return 1f;
		}

		var maxDimension = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
		if (maxDimension <= 0.0001f)
			return 1f;

		return targetMaxDimension / maxDimension;
	}

	private static bool TryGetNodeBounds(Node3D node, Transform3D accumulatedTransform, out Aabb bounds, System.Func<Node3D, bool>? includeNode = null)
	{
		var hasBounds = false;
		bounds = new Aabb();
		if (includeNode != null && !includeNode(node))
			return false;

		var currentTransform = accumulatedTransform * node.Transform;

		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			foreach (var corner in GetCorners(mesh.Mesh.GetAabb()))
			{
				var transformedCorner = currentTransform * corner;
				if (!hasBounds)
				{
					bounds = new Aabb(transformedCorner, Vector3.Zero);
					hasBounds = true;
				}
				else
				{
					bounds = bounds.Expand(transformedCorner);
				}
			}
		}

		foreach (Node child in node.GetChildren())
		{
			if (child is not Node3D childNode || !TryGetNodeBounds(childNode, currentTransform, out var childBounds, includeNode))
				continue;

			foreach (var corner in GetCorners(childBounds))
			{
				if (!hasBounds)
				{
					bounds = new Aabb(corner, Vector3.Zero);
					hasBounds = true;
				}
				else
				{
					bounds = bounds.Expand(corner);
				}
			}
		}

		return hasBounds;
	}

	private static IEnumerable<Vector3> GetCorners(Aabb aabb)
	{
		var position = aabb.Position;
		var end = aabb.End;

		yield return new Vector3(position.X, position.Y, position.Z);
		yield return new Vector3(end.X, position.Y, position.Z);
		yield return new Vector3(position.X, end.Y, position.Z);
		yield return new Vector3(end.X, end.Y, position.Z);
		yield return new Vector3(position.X, position.Y, end.Z);
		yield return new Vector3(end.X, position.Y, end.Z);
		yield return new Vector3(position.X, end.Y, end.Z);
		yield return new Vector3(end.X, end.Y, end.Z);
	}
	
	
	private void EnableShadows(Node node)
	{
		if (node is MeshInstance3D mesh)
		{
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
			//anistropic filtering
			for (int s = 0; s < mesh.GetSurfaceOverrideMaterialCount(); s++)
			{
				var mat = mesh.GetSurfaceOverrideMaterial(s) as StandardMaterial3D;
				if (mat != null)
				{
					mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
					mat.Roughness = Mathf.Max(mat.Roughness, 0.25f);
				}
			}
		}

		foreach (var child in node.GetChildren())
			if (child is Node3D childNode)
				EnableShadows(childNode);
	}

	private static void EnsurePspRenderable(Node node)
	{
		if (node is MeshInstance3D mesh)
		{
			var surfaceCount = mesh.Mesh?.GetSurfaceCount() ?? 0;
			for (int s = 0; s < surfaceCount; s++)
			{
				StandardMaterial3D? material = null;

				if (mesh.GetSurfaceOverrideMaterial(s) is StandardMaterial3D overrideMaterial)
				{
					material = overrideMaterial;
				}
				else if (mesh.Mesh?.SurfaceGetMaterial(s) is StandardMaterial3D baseMaterial)
				{
					material = (StandardMaterial3D)baseMaterial.Duplicate();
					mesh.SetSurfaceOverrideMaterial(s, material);
				}

				if (material == null)
					continue;

				// Some downloaded meshes use mirrored transforms that invert winding.
				// Disabling culling keeps the model visible regardless of winding order.
				material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			}
		}

		foreach (Node child in node.GetChildren())
			EnsurePspRenderable(child);
	}

	private void ApplyGbaScreenLogo(Node node)
	{
		var screenTexture = GetGbaScreenLogoTexture();
		if (screenTexture == null)
			return;

		ApplyGbaLogoRecursive(node, screenTexture);
	}

	private Texture2D? GetGbaScreenLogoTexture()
	{
		if (_gbaScreenLogoTexture != null)
			return _gbaScreenLogoTexture;

		var absolutePath = ProjectSettings.GlobalizePath(GbaScreenLogoPath);
		if (!FileAccess.FileExists(absolutePath))
		{
			GD.PrintErr($"Missing GBA screen logo at {absolutePath}");
			return null;
		}

		var image = Image.LoadFromFile(absolutePath);
		if (image.GetWidth() == 0 || image.GetHeight() == 0)
		{
			GD.PrintErr($"Unable to load GBA screen logo from {absolutePath}");
			return null;
		}

		_gbaScreenLogoTexture = ImageTexture.CreateFromImage(image);
		return _gbaScreenLogoTexture;
	}

	private void ApplyGbaLogoRecursive(Node node, Texture2D screenTexture)
	{
		if (node is MeshInstance3D mesh && mesh.Name.ToString().Contains("Screen"))
		{
			var surfaceCount = mesh.Mesh?.GetSurfaceCount() ?? 0;
			for (int s = 0; s < surfaceCount; s++)
			{
				StandardMaterial3D? material = null;

				if (mesh.GetSurfaceOverrideMaterial(s) is StandardMaterial3D overrideMaterial)
				{
					material = overrideMaterial;
				}
				else if (mesh.Mesh?.SurfaceGetMaterial(s) is StandardMaterial3D baseMaterial)
				{
					material = (StandardMaterial3D)baseMaterial.Duplicate();
					mesh.SetSurfaceOverrideMaterial(s, material);
				}

				if (material == null)
					continue;

				material.AlbedoColor = Colors.White;
				material.AlbedoTexture = screenTexture;
				material.Uv1Scale = new Vector3(GbaScreenUvScaleU, GbaScreenUvScaleV, 1f);
				material.Uv1Offset = new Vector3(GbaScreenUvOffsetU, GbaScreenUvOffsetV, 0f);
				material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
			}
		}

		foreach (var child in node.GetChildren())
			ApplyGbaLogoRecursive((Node)child, screenTexture);
	}

	private void ApplyPspScreenLogo(Node node)
	{
		var screenTexture = GetPspLogoTexture();
		if (screenTexture == null)
			return;

		ApplyPspLogoRecursive(node, screenTexture, node.Name.ToString());
	}

	private Texture2D? GetPspLogoTexture()
	{
		if (_pspScreenLogoTexture != null)
			return _pspScreenLogoTexture;

		var absolutePath = ProjectSettings.GlobalizePath(PspScreenLogoPath);
		if (!FileAccess.FileExists(absolutePath))
		{
			GD.PrintErr($"Missing PSP screen logo at {absolutePath}");
			return null;
		}

		var image = Image.LoadFromFile(absolutePath);
		if (image.GetWidth() == 0 || image.GetHeight() == 0)
		{
			GD.PrintErr($"Unable to load PSP screen logo from {absolutePath}");
			return null;
		}

		_pspScreenLogoTexture = ImageTexture.CreateFromImage(image);
		return _pspScreenLogoTexture;
	}

	private void ApplyPspLogoRecursive(Node node, Texture2D screenTexture, string hierarchyHint)
	{
		var nodeName = node.Name.ToString();
		var currentHint = string.IsNullOrEmpty(hierarchyHint) ? nodeName : $"{hierarchyHint}/{nodeName}";

		if (node is MeshInstance3D mesh)
		{
			var meshName = mesh.Name.ToString();
			var isLikelyScreenMesh =
				currentHint.Contains("psp_Object_54", System.StringComparison.OrdinalIgnoreCase);

			if (isLikelyScreenMesh)
			{
				NormalizeMeshUvsToSingleTile(mesh);
				var targetAspect = EstimateScreenAspect(mesh);
				var surfaceCount = mesh.Mesh?.GetSurfaceCount() ?? 0;
				for (int s = 0; s < surfaceCount; s++)
				{
					StandardMaterial3D? material = null;

					if (mesh.GetSurfaceOverrideMaterial(s) is StandardMaterial3D overrideMaterial)
					{
						material = overrideMaterial;
					}
					else if (mesh.Mesh?.SurfaceGetMaterial(s) is StandardMaterial3D baseMaterial)
					{
						material = (StandardMaterial3D)baseMaterial.Duplicate();
						mesh.SetSurfaceOverrideMaterial(s, material);
					}

					if (material == null)
						continue;

					material.AlbedoColor = Colors.White;
						material.AlbedoTexture = screenTexture;
						material.Metallic = 0f;
						material.Roughness = 0.34f;
						ApplyCenteredCropToMaterial(material, screenTexture, targetAspect);
						material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
						material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
					}
				}
			}

		foreach (var child in node.GetChildren())
			ApplyPspLogoRecursive((Node)child, screenTexture, currentHint);
	}

	private static void NormalizeMeshUvsToSingleTile(MeshInstance3D mesh)
	{
		if (mesh.Mesh is not ArrayMesh sourceMesh)
			return;

		var surfaceCount = sourceMesh.GetSurfaceCount();
		if (surfaceCount <= 0)
			return;

		var normalizedMesh = new ArrayMesh();
		var changed = false;

		for (int s = 0; s < surfaceCount; s++)
		{
			var arrays = sourceMesh.SurfaceGetArrays(s);
			if (arrays.Count > (int)Mesh.ArrayType.TexUV)
			{
				var sourceUvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
				if (sourceUvs.Length > 0)
				{
					var minU = float.PositiveInfinity;
					var maxU = float.NegativeInfinity;
					var minV = float.PositiveInfinity;
					var maxV = float.NegativeInfinity;

					for (int i = 0; i < sourceUvs.Length; i++)
					{
						var uv = sourceUvs[i];
						minU = Mathf.Min(minU, uv.X);
						maxU = Mathf.Max(maxU, uv.X);
						minV = Mathf.Min(minV, uv.Y);
						maxV = Mathf.Max(maxV, uv.Y);
					}

					var rangeU = maxU - minU;
					var rangeV = maxV - minV;
					if (rangeU > 0.0001f && rangeV > 0.0001f)
					{
						var normalizedUvs = new Vector2[sourceUvs.Length];
							for (int i = 0; i < sourceUvs.Length; i++)
							{
								var uv = sourceUvs[i];
								normalizedUvs[i] = new Vector2(
									(uv.X - minU) / rangeU,
									1f - ((uv.Y - minV) / rangeV));
							}

						arrays[(int)Mesh.ArrayType.TexUV] = normalizedUvs;
						changed = true;
					}
				}
			}

			normalizedMesh.AddSurfaceFromArrays(sourceMesh.SurfaceGetPrimitiveType(s), arrays);
			if (sourceMesh.SurfaceGetMaterial(s) is Material sourceMaterial)
				normalizedMesh.SurfaceSetMaterial(s, sourceMaterial);
		}

		if (changed)
			mesh.Mesh = normalizedMesh;
	}

	private static float EstimateScreenAspect(MeshInstance3D mesh)
	{
		if (mesh.Mesh == null)
			return 16f / 9f;

		var size = mesh.Mesh.GetAabb().Size;
		var dims = new[]
		{
			Mathf.Abs(size.X),
			Mathf.Abs(size.Y),
			Mathf.Abs(size.Z),
		};
		System.Array.Sort(dims);

		var shortSide = Mathf.Max(dims[1], 0.0001f);
		return dims[2] / shortSide;
	}

	private static void ApplyCenteredCropToMaterial(StandardMaterial3D material, Texture2D texture, float targetAspect)
	{
		var textureWidth = texture.GetWidth();
		var textureHeight = texture.GetHeight();
		if (textureWidth <= 0 || textureHeight <= 0 || targetAspect <= 0f)
		{
			material.Uv1Scale = Vector3.One;
			material.Uv1Offset = Vector3.Zero;
			return;
		}

		var textureAspect = (float)textureWidth / textureHeight;
		var scaleU = 1f;
		var scaleV = 1f;

		if (textureAspect > targetAspect)
		{
			// Image is wider: crop left/right and keep center.
			scaleU = targetAspect / textureAspect;
		}
		else if (textureAspect < targetAspect)
		{
			// Image is taller: crop top/bottom and keep center.
			scaleV = textureAspect / targetAspect;
		}

		var offsetU = ((1f - scaleU) * 0.5f) + PspScreenNudgeLeftU;
		var offsetV = (1f - scaleV) * 0.5f;

		material.Uv1Scale = new Vector3(scaleU, scaleV, 1f);
		material.Uv1Offset = new Vector3(offsetU, offsetV, 0f);
	}

	private void ConfigureConsoleAnimation(Node3D wrapper, Node3D model, ConsoleType type)
	{
		var animationPlayer = FindAnimationPlayer(model);
		if (animationPlayer == null)
			return;

		var animationName = string.Empty;
		foreach (var candidate in animationPlayer.GetAnimationList())
		{
			animationName = candidate.ToString();
			if (!string.IsNullOrEmpty(animationName))
				break;
		}

		if (string.IsNullOrEmpty(animationName))
			return;

		var animation = animationPlayer.GetAnimation(animationName);
		if (animation == null)
			return;

		animation.LoopMode = Animation.LoopModeEnum.None;
		animationPlayer.Play(animationName);
		animationPlayer.Seek(atRest(type, animation.Length), true);
		animationPlayer.Stop(true);

		_consoleAnimations[wrapper] = animationPlayer;
		_consoleAnimationNames[wrapper] = animationName;
		_consoleAnimationLengths[wrapper] = animation.Length;
		_consoleAnimationHoldTimes[wrapper] = GetConsoleConfiguredHoldTime(type, animation.Length);
		if (type == ConsoleType.GameCube)
			SetGameCubeDiskVisible(model, false);
		if (type == ConsoleType.PlayStation2)
			SetPs2DiskVisible(model, false);
		if (type == ConsoleType.SNES)
			SetSnesCartridgeVisible(model, false);
		if (type == ConsoleType.NES)
			SetNesCartridgeVisible(model, false);
	}

	private static AnimationPlayer? FindAnimationPlayer(Node node)
	{
		if (node is AnimationPlayer animationPlayer)
			return animationPlayer;

		foreach (Node child in node.GetChildren())
		{
			var nestedPlayer = FindAnimationPlayer(child);
			if (nestedPlayer != null)
				return nestedPlayer;
		}

		return null;
	}

	private static bool ShouldAutoOpenAnimatedConsole(Node3D box)
	{
		return !IsSelectOnlyAnimatedConsole(box);
	}

	private static bool IsSelectOnlyAnimatedConsole(Node3D box)
	{
		return IsConsoleType(box, ConsoleType.Wii) ||
			IsConsoleType(box, ConsoleType.Nintendo64) ||
			IsConsoleType(box, ConsoleType.SNES) ||
			IsConsoleType(box, ConsoleType.NES) ||
			IsConsoleType(box, ConsoleType.GameCube) ||
			IsConsoleType(box, ConsoleType.PlayStation2);
	}

	public async Task PlaySelectedConsoleAnimationAsync(ConsoleType requiredType)
	{
		if (_boxes.Count == 0)
			return;

		var selectedIdx = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (selectedIdx < 0 || selectedIdx >= _boxes.Count)
			return;

		var selectedBox = _boxes[selectedIdx];
		if (!IsConsoleType(selectedBox, requiredType) ||
			!_consoleAnimations.TryGetValue(selectedBox, out var animationPlayer) ||
			!_consoleAnimationNames.TryGetValue(selectedBox, out var animationName))
		{
			return;
		}

		var animationLength = _consoleAnimationLengths.GetValueOrDefault(selectedBox, 0.0);
		_selectionAnimatedConsoles.Add(selectedBox);
		var holdTime = GetConsoleAnimationHoldTime(selectedBox, animationLength);
		if (holdTime <= 0.0)
			return;
		var startTime = IsConsoleType(selectedBox, ConsoleType.GBA) && _openAnimatedConsoles.Contains(selectedBox)
			? Mathf.Min((float)GbaOpen, (float)holdTime)
			: 0.0;


		ResetSelectionRotation(selectedBox, selectedIdx);
		_manuallyClosedAnimatedConsoles.Remove(selectedBox);
		_pendingConsoleCloseSounds.Remove(selectedBox);
		_openAnimatedConsoles.Add(selectedBox);
		if (requiredType == ConsoleType.Wii)
			SetWiiDiskVisible(selectedBox, true);
		if (requiredType == ConsoleType.Nintendo64)
			SetN64CartridgeVisible(selectedBox, true);
		if (requiredType == ConsoleType.SNES)
			SetSnesCartridgeVisible(selectedBox, true);
		if (requiredType == ConsoleType.NES)
			SetNesCartridgeVisible(selectedBox, true);
		if (requiredType == ConsoleType.GameCube)
			SetGameCubeDiskVisible(selectedBox, true);
		if (requiredType == ConsoleType.PlayStation2)
			SetPs2DiskVisible(selectedBox, true);
		if (requiredType == ConsoleType.GBA)
			SetGbaCartridgeVisible(selectedBox, true);

		animationPlayer.Stop();
		animationPlayer.SpeedScale = SelectionAnimationSpeedScale;
		animationPlayer.Play(animationName);
		animationPlayer.Seek(startTime, true);

		var waitTime = Mathf.Max((float)((holdTime - startTime) / SelectionAnimationSpeedScale), 0.05f);
		await ToSignal(GetTree().CreateTimer(waitTime), SceneTreeTimer.SignalName.Timeout);
		if (!GodotObject.IsInstanceValid(this) ||
			!GodotObject.IsInstanceValid(selectedBox) ||
			!GodotObject.IsInstanceValid(animationPlayer))
		{
			return;
		}

		animationPlayer.SpeedScale = 1f;
		SetConsoleAnimationPose(selectedBox, holdTime, holdPose: true);
		if (requiredType == ConsoleType.PlayStation2)
			SetPs2DiskVisible(selectedBox, true);
	}

	private void ResetSelectionRotation(Node3D selectedBox, int selectedIdx)
	{
		if (_hoveredIdx == selectedIdx)
			_hoveredIdx = -1;

		_hoverTween?.Kill();
		_hoverTween = null;
		selectedBox.Rotation = Vector3.Zero;
	}

	public void PlaySelectedConsoleReturnAnimation(ConsoleType requiredType)
	{
		if (_boxes.Count == 0)
			return;

		var selectedIdx = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (selectedIdx < 0 || selectedIdx >= _boxes.Count)
			return;

		var selectedBox = _boxes[selectedIdx];
		if (!IsConsoleType(selectedBox, requiredType) ||
			!_consoleAnimations.TryGetValue(selectedBox, out var animationPlayer) ||
			!_consoleAnimationNames.TryGetValue(selectedBox, out var animationName))
		{
			return;
		}

		var animationLength = _consoleAnimationLengths.GetValueOrDefault(selectedBox, 0.0);
		var startTime = GetConsoleSelectionAnimationHoldTime(selectedBox, animationLength);
		var stopTime = _consoleAnimationHoldTimes.TryGetValue(selectedBox, out var configuredHoldTime)
			? configuredHoldTime
			: animationLength;
		if (startTime <= stopTime)
			return;

		_selectionAnimatedConsoles.Remove(selectedBox);
		_manuallyClosedAnimatedConsoles.Remove(selectedBox);
		_pendingConsoleCloseSounds.Remove(selectedBox);
		_openAnimatedConsoles.Add(selectedBox);
		_returnAnimatedConsoles.Add(selectedBox);
		if (requiredType == ConsoleType.GBA)
			SetGbaCartridgeVisible(selectedBox, true);

		animationPlayer.Play(animationName);
		animationPlayer.Seek(startTime, true);
		animationPlayer.PlayBackwards(animationName);
	}

	private void UpdateAnimatedConsoleStates(int selectedIdx)
	{
		if (_boxes.Count == 0)
			return;

		var selectedBox = selectedIdx >= 0 && selectedIdx < _boxes.Count
			? _boxes[selectedIdx]
			: null;

		foreach (var box in _boxes)
		{
			if (!_consoleAnimations.ContainsKey(box))
				continue;

			if (!ShouldAutoOpenAnimatedConsole(box))
			{
				if (box != selectedBox && _openAnimatedConsoles.Contains(box))
					CloseAnimatedConsole(box);
				continue;
			}

			if (box != selectedBox)
				_manuallyClosedAnimatedConsoles.Remove(box);

			var shouldBeOpen =
				box == selectedBox &&
				!_manuallyClosedAnimatedConsoles.Contains(box);
			var isOpen = _openAnimatedConsoles.Contains(box);
			if (shouldBeOpen == isOpen)
				continue;

			if (shouldBeOpen)
			{
				OpenAnimatedConsole(box);
			}
			else
			{
				CloseAnimatedConsole(box);
			}
		}
	}

	private void UpdateAnimatedConsolePlayback()
	{
		foreach (var (box, animationPlayer) in _consoleAnimations)
		{
			if (!_consoleAnimationLengths.TryGetValue(box, out var animationLength) || animationLength <= 0.0)
				continue;

			var holdTime = GetConsoleAnimationHoldTime(box, animationLength);

			if (_returnAnimatedConsoles.Contains(box))
			{
				var returnHoldTime = _consoleAnimationHoldTimes.TryGetValue(box, out var configuredHoldTime)
					? configuredHoldTime
					: animationLength;
				if (!animationPlayer.IsPlaying() ||
					animationPlayer.CurrentAnimationPosition <= returnHoldTime + 0.02)
				{
					_returnAnimatedConsoles.Remove(box);
					SetConsoleAnimationPose(box, returnHoldTime, holdPose: true);
					if (IsConsoleType(box, ConsoleType.GBA))
						SetGbaCartridgeVisible(box, false);
				}
				continue;
			}

			if (_openAnimatedConsoles.Contains(box))
			{
				if (!animationPlayer.IsPlaying() ||
					animationPlayer.CurrentAnimationPosition >= holdTime - 0.02)
				{
					SetConsoleAnimationPose(box, holdTime, holdPose: true);
				}
				continue;
			}

			if (_selectionAnimatedConsoles.Contains(box) && animationPlayer.IsPlaying())
				continue;

			var defaultPoseTime = atRest(box, animationLength);
			var isClosed = !animationPlayer.IsPlaying() ||
				animationPlayer.CurrentAnimationPosition <= defaultPoseTime + 0.02;
			if (_pendingConsoleCloseSounds.Contains(box) && isClosed)
			{
				AudioManager.Instance?.PlayConsoleAnimationSfx(AudioManager.GbaCloseSfxPath);
				_pendingConsoleCloseSounds.Remove(box);
			}

			if (isClosed)
			{
				SetConsoleAnimationPose(box, defaultPoseTime, holdPose: false);
				if (IsConsoleType(box, ConsoleType.Wii))
					SetWiiDiskVisible(box, false);
				if (IsConsoleType(box, ConsoleType.Nintendo64))
					SetN64CartridgeVisible(box, false);
				if (IsConsoleType(box, ConsoleType.SNES))
					SetSnesCartridgeVisible(box, false);
				if (IsConsoleType(box, ConsoleType.NES))
					SetNesCartridgeVisible(box, false);
				if (IsConsoleType(box, ConsoleType.GameCube))
					SetGameCubeDiskVisible(box, false);
				if (IsConsoleType(box, ConsoleType.PlayStation1))
					SetPs1DiskVisible(box, false);
				if (IsConsoleType(box, ConsoleType.PlayStation2))
					SetPs2DiskVisible(box, false);
				if (IsConsoleType(box, ConsoleType.PSP))
					SetPspDiskVisible(box, false);
				if (IsConsoleType(box, ConsoleType.GBA))
					SetGbaCartridgeVisible(box, false);
			}
		}
	}

	private void SetConsoleAnimationPose(Node3D box, double animationPosition, bool holdPose)
	{
		if (!_consoleAnimations.TryGetValue(box, out var animationPlayer) ||
			!_consoleAnimationNames.TryGetValue(box, out var animationName))
		{
			return;
		}

		animationPlayer.Play(animationName);
		animationPlayer.Seek(animationPosition, true);
		if (holdPose)
		{
			animationPlayer.Pause();
		}
		else
		{
			animationPlayer.Stop(true);
		}
	}

	private double GetConsoleAnimationHoldTime(Node3D box, double animationLength)
	{
		if (_selectionAnimatedConsoles.Contains(box))
			return GetConsoleSelectionAnimationHoldTime(box, animationLength);

		return _consoleAnimationHoldTimes.TryGetValue(box, out var configuredHoldTime)
			? configuredHoldTime
			: animationLength;
	}

	private static double GetConsoleSelectionAnimationHoldTime(Node3D box, double animationLength)
	{
		if (IsConsoleType(box, ConsoleType.GBA))
			return Mathf.Min((float)animationLength, (float)GbaSelectionOpen);
		if (IsConsoleType(box, ConsoleType.Nintendo64))
			return Mathf.Min((float)animationLength, (float)N64SelectionOpen);
		if (IsConsoleType(box, ConsoleType.GameCube))
			return Mathf.Min((float)animationLength, (float)GameCubeRestPose);

		return animationLength;
	}

	private static double GetConsoleConfiguredHoldTime(ConsoleType type, double animationLength)
	{
		return type switch
		{
			ConsoleType.GBA => Mathf.Min((float)animationLength, (float)GbaOpen),
			ConsoleType.GameCube => Mathf.Min((float)animationLength, (float)GameCubeRestPose),
			_ => animationLength
		};
	}

	private static double atRest(ConsoleType type, double animationLength)
	{
		if (type == ConsoleType.NintendoDS)
			return animationLength * 0.5;

		return type == ConsoleType.GameCube
			? Mathf.Min((float)animationLength, (float)GameCubeRestPose)
			: 0.0;
	}

	private static double atRest(Node3D box, double animationLength)
	{
		if (IsConsoleType(box, ConsoleType.NintendoDS))
			return animationLength * 0.5;

		return IsConsoleType(box, ConsoleType.GameCube)
			? Mathf.Min((float)animationLength, (float)GameCubeRestPose)
			: 0.0;
	}

	private void OpenAnimatedConsole(Node3D box)
	{
		if (!_consoleAnimations.TryGetValue(box, out var animationPlayer) ||
			!_consoleAnimationNames.TryGetValue(box, out var animationName))
		{
			return;
		}

		_manuallyClosedAnimatedConsoles.Remove(box);
		_pendingConsoleCloseSounds.Remove(box);
		if (IsConsoleType(box, ConsoleType.Wii))
			SetWiiDiskVisible(box, true);
		if (IsConsoleType(box, ConsoleType.Nintendo64))
			SetN64CartridgeVisible(box, true);
		if (IsConsoleType(box, ConsoleType.SNES))
			SetSnesCartridgeVisible(box, true);
		if (IsConsoleType(box, ConsoleType.NES))
			SetNesCartridgeVisible(box, true);
		if (IsConsoleType(box, ConsoleType.NintendoDS) &&
			_consoleAnimationLengths.TryGetValue(box, out var animationLength))
		{
			animationPlayer.Seek(atRest(box, animationLength), true);
		}
		animationPlayer.Play(animationName);
		_openAnimatedConsoles.Add(box);
		if (IsConsoleType(box, ConsoleType.GBA) || IsConsoleType(box, ConsoleType.NintendoDS))
			AudioManager.Instance?.PlayConsoleAnimationSfx(AudioManager.GbaOpenSfxPath);
	}

	private void CloseAnimatedConsole(Node3D box, bool manual = false)
	{
		if (!_consoleAnimations.TryGetValue(box, out var animationPlayer) ||
			!_consoleAnimationNames.TryGetValue(box, out var animationName))
		{
			return;
		}

		if (manual)
			_manuallyClosedAnimatedConsoles.Add(box);
		else
			_manuallyClosedAnimatedConsoles.Remove(box);
		_selectionAnimatedConsoles.Remove(box);
		_returnAnimatedConsoles.Remove(box);

		if (IsConsoleType(box, ConsoleType.PlayStation2))
			SetPs2DiskVisible(box, false);
		if (IsConsoleType(box, ConsoleType.PlayStation1))
			SetPs1DiskVisible(box, false);
		if (IsConsoleType(box, ConsoleType.PSP))
			SetPspDiskVisible(box, false);
		if (IsConsoleType(box, ConsoleType.SNES))
			SetSnesCartridgeVisible(box, false);
		if (IsConsoleType(box, ConsoleType.NES))
			SetNesCartridgeVisible(box, false);
		if (_consoleAnimationHoldTimes.TryGetValue(box, out var holdTime))
			animationPlayer.Seek(holdTime, true);
		animationPlayer.PlayBackwards(animationName);
		_openAnimatedConsoles.Remove(box);
		if (IsConsoleType(box, ConsoleType.GBA) || IsConsoleType(box, ConsoleType.NintendoDS))
			_pendingConsoleCloseSounds.Add(box);
		else
			_pendingConsoleCloseSounds.Remove(box);
	}

	private static bool IsConsoleType(Node3D box, ConsoleType type)
	{
		return string.Equals(box.Name.ToString(), type.ToString(), System.StringComparison.Ordinal);
	}

	private static bool ShouldIncludeWiiBodyBounds(Node3D node)
	{
		return !IsWiiDiskNode(node);
	}

	private static bool IsWiiDiskNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		return string.Equals(nodeName, "Object_2", System.StringComparison.OrdinalIgnoreCase);
	}

	private static void SetWiiDiskVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsWiiDiskNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetWiiDiskVisible(child, visible);
	}

	private static bool IsGameCubeDiskNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		if (nodeName.Contains("gamecube_Object_0_001", System.StringComparison.OrdinalIgnoreCase) ||
			nodeName.Contains("Object_0.001", System.StringComparison.OrdinalIgnoreCase) ||
			nodeName.Contains("Object_0_001", System.StringComparison.OrdinalIgnoreCase) ||
			nodeName.Contains("Object_2.001", System.StringComparison.OrdinalIgnoreCase) ||
			nodeName.Contains("Object_2_001", System.StringComparison.OrdinalIgnoreCase) ||
			nodeName.Contains("Disc.obj", System.StringComparison.OrdinalIgnoreCase) ||
			nodeName.Contains("Disc_obj", System.StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return node is MeshInstance3D mesh &&
			mesh.Mesh != null &&
			(mesh.Mesh.ResourceName.Contains("gamecube_Object_0_001", System.StringComparison.OrdinalIgnoreCase) ||
				mesh.Mesh.ResourceName.Contains("Object_0.001", System.StringComparison.OrdinalIgnoreCase) ||
				mesh.Mesh.ResourceName.Contains("Object_0_001", System.StringComparison.OrdinalIgnoreCase));
	}

	private static void SetGameCubeDiskVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsGameCubeDiskNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetGameCubeDiskVisible(child, visible);
	}

	private static bool IsPs2DiskNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		if (string.Equals(nodeName, "ps2_Object_0", System.StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return node is MeshInstance3D mesh &&
			mesh.Mesh != null &&
			mesh.Mesh.ResourceName.Contains("ps2_Object_0", System.StringComparison.OrdinalIgnoreCase);
	}

	private static void SetPs2DiskVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsPs2DiskNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetPs2DiskVisible(child, visible);
	}

	private static bool IsPs1DiskNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		if (string.Equals(nodeName, "ps1_Object_0", System.StringComparison.OrdinalIgnoreCase))
			return true;

		return node is MeshInstance3D mesh &&
			mesh.Mesh != null &&
			mesh.Mesh.ResourceName.Contains("ps1_Object_0", System.StringComparison.OrdinalIgnoreCase);
	}

	private static void SetPs1DiskVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsPs1DiskNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetPs1DiskVisible(child, visible);
	}

	private static bool IsPspDiskNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		if (string.Equals(nodeName, "psp_path2120", System.StringComparison.OrdinalIgnoreCase))
			return true;

		return node is MeshInstance3D mesh &&
			mesh.Mesh != null &&
			mesh.Mesh.ResourceName.Contains("psp_path2120", System.StringComparison.OrdinalIgnoreCase);
	}

	private static void SetPspDiskVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsPspDiskNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetPspDiskVisible(child, visible);
	}

	private static bool IsGbaCartridgeNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		if (nodeName.Contains("gba_Object_0", System.StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nodeName, "Object_4", System.StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return node is MeshInstance3D mesh &&
			mesh.Mesh != null &&
			mesh.Mesh.ResourceName.Contains("Object_0", System.StringComparison.OrdinalIgnoreCase);
	}

	private static void SetGbaCartridgeVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsGbaCartridgeNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetGbaCartridgeVisible(child, visible);
	}

	private static void SetN64CartridgeVisible(Node node, bool visible)
	{
		if (node is Node3D node3D &&
			node3D.Name.ToString().Contains("cartridge", System.StringComparison.OrdinalIgnoreCase))
		{
			node3D.Visible = visible;
		}

		foreach (Node child in node.GetChildren())
			SetN64CartridgeVisible(child, visible);
	}

	private static bool IsSnesCartridgeNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		if (nodeName.Contains("SNES_Cart_Chrono_T|SNES_Cart|Dupli|0_1_SNES_Cart_Rough_0", System.StringComparison.OrdinalIgnoreCase) ||
			nodeName.Contains("SNES_Cart_Rough_0", System.StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return node is MeshInstance3D mesh &&
			mesh.Mesh != null &&
			(mesh.Mesh.ResourceName.Contains("SNES_Cart_Chrono_T|SNES_Cart|Dupli|0_1_SNES_Cart_Rough_0", System.StringComparison.OrdinalIgnoreCase) ||
				mesh.Mesh.ResourceName.Contains("SNES_Cart_Rough_0", System.StringComparison.OrdinalIgnoreCase));
	}

	private static void SetSnesCartridgeVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsSnesCartridgeNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetSnesCartridgeVisible(child, visible);
	}

	private static bool IsNesCartridgeNode(Node3D node)
	{
		var nodeName = node.Name.ToString();
		if (nodeName.Contains("NES_Bolt_001", System.StringComparison.OrdinalIgnoreCase))
			return true;

		return node is MeshInstance3D mesh &&
			mesh.Mesh != null &&
			mesh.Mesh.ResourceName.Contains("NES_Bolt_001", System.StringComparison.OrdinalIgnoreCase);
	}

	private static void SetNesCartridgeVisible(Node node, bool visible)
	{
		if (node is Node3D node3D && IsNesCartridgeNode(node3D))
			node3D.Visible = visible;

		foreach (Node child in node.GetChildren())
			SetNesCartridgeVisible(child, visible);
	}

	private bool TryToggleSelectedConsole(Vector2 localPos)
	{
		if (IsCarouselMoving() ||
			localPos.X < Size.X * 0.2f || localPos.X > Size.X * 0.8f ||
			localPos.Y < Size.Y * 0.15f || localPos.Y > Size.Y * 0.9f)
		{
			return false;
		}

		var selectedIdx = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (selectedIdx < 0 || selectedIdx >= _boxes.Count)
			return false;

		var selectedBox = _boxes[selectedIdx];
		if (!_consoleAnimations.ContainsKey(selectedBox))
			return false;
		if (IsSelectOnlyAnimatedConsole(selectedBox))
		{
			return false;
		}

		if (_openAnimatedConsoles.Contains(selectedBox))
		{
			CloseAnimatedConsole(selectedBox, manual: true);
		}
		else
		{
			OpenAnimatedConsole(selectedBox);
		}

		return true;
	}
	
	
	// Physics processing for the spin
	public override void _Process(double delta)
	{
		_mouseIdleTime += delta;

		if (IsCarouselMoving() && _hoveredIdx != -1)
		{
			OnBoxHoverExit(_hoveredIdx);
			_hoveredIdx = -1;
		}
		
		if (!_dragging && Mathf.Abs(_velocity) > 0.001f)
		{
			CarouselPos += _velocity * (float)delta;
			CarouselPos = WrapPos(CarouselPos);
			UpdateSpinAudioFromMotion();
			_velocity = Mathf.Lerp(_velocity, 0f, Friction * (float)delta);

			// Snap when nearly stopped
			if (Mathf.Abs(_velocity) < 0.05f)
			{
				_velocity = 0f;
				var nearest = Mathf.Round(CarouselPos);
				CarouselPos = WrapPos(nearest);
				SelectionChanged?.Invoke(WrapIndex(Mathf.RoundToInt(CarouselPos)));
				ElasticSnapSelected();
				PlaySpinAudio(suppressIfRecent: true);
			}
		}

		// Idle spin on selected cartridge
		var selectedIdx = WrapIndex(Mathf.RoundToInt(CarouselPos));
		UpdateAnimatedConsoleStates(selectedIdx);
		UpdateAnimatedConsolePlayback();
		for (int i = 0; i < _boxes.Count; i++)
		{
			if (i == selectedIdx && !_dragging && _mouseIdleTime > MouseIdleThreshold)
				_boxes[i].RotateY((float)delta * 0.4f);
		}

		LayoutBoxes();
	}

	public override void _ExitTree()
	{
		AudioManager.Instance?.StopCarouselHover(false);
	}
	
	
	// Handle when the mouse is clicked or draggged, kills velocity so it doesnt drift when you drag
	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Position.Y < 70f)
			{
				MouseFilter = MouseFilterEnum.Pass;
				return;
			}
			
			if (mb.Pressed)
			{
				_dragging = true;
				MouseFilter = MouseFilterEnum.Stop;
				AudioManager.Instance?.StopCarouselHover();
				_dragStartX = mb.Position.X;
				_dragStartPos = CarouselPos;
				_dragStartMousePos = mb.Position;
				_dragMoved = false;
				_lastDragX = mb.Position.X;
				_lastDragVelocity = 0f;
				_velocity = 0f;
				GetViewport().SetInputAsHandled();
			}
			else if (_dragging)
			{
				var wasClick = !_dragMoved && mb.Position.DistanceTo(_dragStartMousePos) <= ClickDragThreshold;
				_dragging = false;
				MouseFilter = MouseFilterEnum.Pass;
				if (wasClick && TryToggleSelectedConsole(mb.Position))
				{
					_velocity = 0f;
					GetViewport().SetInputAsHandled();
					return;
				}

				// Transfer drag velocity to physics
				_velocity = _lastDragVelocity;
			}
		}

		if (_dragging && e is InputEventMouseMotion mm)
		{
			var dx = mm.Position.X - _dragStartX;
			if (!_dragMoved && mm.Position.DistanceTo(_dragStartMousePos) > ClickDragThreshold)
				_dragMoved = true;
			CarouselPos = WrapPos(_dragStartPos - dx * DragScale * 5f);
			UpdateSpinAudioFromMotion();

			// Track velocity for fling
			_lastDragVelocity = (mm.Position.X - _lastDragX) * -0.001f * FlingMultiplier * 30f;
			_lastDragX = mm.Position.X;

			SelectionChanged?.Invoke(WrapIndex(Mathf.RoundToInt(CarouselPos)));
		}
		
		// Mouse wheel stepping
		if (e is InputEventMouseButton wheel && wheel.Pressed)
		{
			if (wheel.ButtonIndex == MouseButton.WheelUp) StepDirection(-1);
			if (wheel.ButtonIndex == MouseButton.WheelDown) StepDirection(1);
		}
	}
	
	// Handle when the mouse hovers over the box
	public override void _Input(InputEvent e)
	{
		if (_boxes.Count == 0) return;
		if (e is InputEventMouseMotion mm)
		{
			_mouseIdleTime = 0f; // reset on any mouse movement
			
			
			var selectedIdx = WrapIndex(Mathf.RoundToInt(CarouselPos));
			var localPos = GetLocalMousePosition();
			var isOverCenter = localPos.X > Size.X * 0.2f && localPos.X < Size.X * 0.8f;
			var canHover = !IsCarouselMoving();
			
			if (canHover && isOverCenter && _hoveredIdx != selectedIdx)
			{
				_hoveredIdx = selectedIdx;
				OnBoxHoverEnter(selectedIdx, localPos);
			}
			else if ((!canHover || !isOverCenter) && _hoveredIdx != -1)
			{
				OnBoxHoverExit(_hoveredIdx);
				_hoveredIdx = -1;
			}
			else if (canHover && isOverCenter && _hoveredIdx == selectedIdx)
			{
				// Update tilt based on mouse position within the card
				UpdateHoverTilt(selectedIdx, localPos);
			}

		}
		

	}
	
	// Methods for animating the boxes whenever they are hovered over
	private void OnBoxHoverEnter(int idx, Vector2 mousePos)
	{
		if (idx >= _boxes.Count) return;
		var box = _boxes[idx];

		if (!_dragging)
			AudioManager.Instance?.PlayCarouselHover();

		_hoverTween?.Kill();
		_hoverTween = CreateTween();
		_hoverTween.SetTrans(Tween.TransitionType.Back);
		_hoverTween.SetEase(Tween.EaseType.Out);
		_hoverTween.TweenProperty(box, "scale",
			new Vector3(SelectedScale * 1.08f, SelectedScale * 1.08f, SelectedScale * 1.08f),
			0.2f);
	}

	private void OnBoxHoverExit(int idx)
	{
		if (idx >= _boxes.Count) return;
		var box = _boxes[idx];

		AudioManager.Instance?.StopCarouselHover();

		_hoverTween?.Kill();
		_hoverTween = CreateTween();
		_hoverTween.SetTrans(Tween.TransitionType.Spring);
		_hoverTween.SetEase(Tween.EaseType.Out);
		_hoverTween.TweenProperty(box, "scale",
			new Vector3(SelectedScale, SelectedScale, SelectedScale),
			0.6f);
		// Reset tilt
		_hoverTween.TweenProperty(box, "rotation",
			new Vector3(0f, 0f, 0f),
			0.4f);
	}

	private void UpdateHoverTilt(int idx, Vector2 mousePos)
	{
		if (idx >= _boxes.Count) return;
		var box = _boxes[idx];

		// Map mouse position to tilt angle — center = no tilt, edges = max tilt
		var nx = (mousePos.X / Size.X - 0.5f) * 2f;  // -1 to 1
		var ny = (mousePos.Y / Size.Y - 0.5f) * 2f;  // -1 to 1

		var tiltX = Mathf.DegToRad(-ny * 12f);  // tilt up/down
		var tiltY = Mathf.DegToRad( nx * 12f);  // tilt left/right

		// Smoothly interpolate current rotation toward target
		var currentRot = box.Rotation;
		box.Rotation = new Vector3(
			Mathf.Lerp(currentRot.X, tiltX, 0.15f),
			Mathf.Lerp(currentRot.Y, tiltY, 0.15f),
			currentRot.Z
		);
	}
	
	// Method for snapping the game into place more fluidly after a swipe
	private void ElasticSnapSelected()
	{
		var idx = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (idx >= _boxes.Count) return;
		var box = _boxes[idx];

		_hoverTween?.Kill();
		_hoverTween = CreateTween();
		_hoverTween.SetTrans(Tween.TransitionType.Elastic);
		_hoverTween.SetEase(Tween.EaseType.Out);
		_hoverTween.TweenProperty(box, "scale",
			new Vector3(SelectedScale * 1.1f, SelectedScale * 1.1f, SelectedScale * 1.1f),
			0.05f); // tiny quick punch up
		_hoverTween.TweenProperty(box, "scale",
			new Vector3(SelectedScale, SelectedScale, SelectedScale),
			0.6f); // elastic settle back
	}
	
	// Wrapping logic: d is how far the box is from teh center and t prevents distortion with distnace
	private void LayoutBoxes()
	{
		if (_boxes.Count == 0) return;

		for (int i = 0; i < _boxes.Count; i++)
		{
			var box = _boxes[i];

			var d = i - CarouselPos;
			if (d > _count * 0.5f) d -= _count;
			if (d < -_count * 0.5f) d += _count;

			var t = Mathf.Clamp(Mathf.Abs(d), 0f, 1.5f);

			box.Position = new Vector3(d * Spacing, t *0.4f, -t * 1.2f);
			
			// ONLY set scale and rotation if not hovered
			if (i != _hoveredIdx)
			{
				var scale = Mathf.Lerp(SelectedScale, UnselectedScale, t);
				box.Scale = new Vector3(scale, scale, scale);
				box.Rotation = new Vector3(0, Mathf.DegToRad(d * -8f), 0);
			}
			
			var brightness = Mathf.Lerp(1.0f, 0.3f, t);
			DimMeshes(box, brightness);
			
		}
	}
	
	// Dims the models that are in the background
	private void DimMeshes(Node node, float brightness)
	{
		if (node is MeshInstance3D mesh)
		{
			for (int s = 0; s < mesh.GetSurfaceOverrideMaterialCount(); s++)
			{
				// Get or create an override material per surface
				if (mesh.GetSurfaceOverrideMaterial(s) is not StandardMaterial3D mat)
				{
					// Duplicate the base material 
					if (mesh.Mesh?.SurfaceGetMaterial(s) is StandardMaterial3D baseMat)
					{
						mat = (StandardMaterial3D)baseMat.Duplicate();
						mesh.SetSurfaceOverrideMaterial(s, mat);
						_originalColors[mat] = mat.AlbedoColor;
					}
					else continue;
				}
				if (!_originalColors.ContainsKey(mat))
					_originalColors[mat] = mat.AlbedoColor;
				
				var original = _originalColors[mat];
				
				mat.AlbedoColor = new Color(
					original.R * brightness,
					original.G * brightness,
					original.B * brightness, 
					original.A);
			}
		}
		foreach (Node child in node.GetChildren())
			DimMeshes(child, brightness);
	}

	private float WrapPos(float p)
	{
		if (_count == 0) return 0f;
		p %= _count;
		if (p < 0) p += _count;
		return p;
	}

	private int WrapIndex(int i)
	{
		if (_count == 0) return 0;
		i %= _count;
		if (i < 0) i += _count;
		return i;
	}

	private void UpdateSpinAudioFromMotion()
	{
		if (_count == 0)
			return;

		var delta = CarouselPos - _lastSpinAudioCarouselPos;
		var halfCount = _count * 0.5f;

		if (delta > halfCount)
			delta -= _count;
		else if (delta < -halfCount)
			delta += _count;

		if (Mathf.IsZeroApprox(delta))
			return;

		_spinAudioPos += delta;
		_lastSpinAudioCarouselPos = CarouselPos;
		
		var speedSource = _dragging ? Mathf.Abs(_lastDragVelocity) : Mathf.Abs(_velocity);
		_spinSpeed = Mathf.Clamp(speedSource / 4.0f, 0f, 1f);

		var currentStep = Mathf.RoundToInt(_spinAudioPos);
		if (currentStep == _lastSpinAudioStep)
			return;

		_lastSpinAudioStep = currentStep;
		PlaySpinAudio(true);
	}

	private void PlaySpinAudio(bool suppressIfRecent = false)
	{
		var now = Time.GetTicksMsec();
		if (suppressIfRecent && now - _lastSpinAudioMs < SettleSpinSuppressWindowMs)
			return;

		var pitch = Mathf.Lerp(0.9f, 1.6f, _spinSpeed);
		AudioManager.Instance?.PlayCarouselSpin(pitch);
		_lastSpinAudioMs = now;
	}

	private bool IsCarouselMoving()
	{
		return _dragging || Mathf.Abs(_velocity) > HoverMotionThreshold;
	}
	
	public void StepDirection(int dir)
	{
		AudioManager.Instance?.StopCarouselHover();
		var current = Mathf.RoundToInt(CarouselPos);
		var target = WrapPos(current + dir);
		_velocity = 0f;
		CarouselPos = WrapPos(current);
		
		_velocity = dir * 3.5f;
	}
}
