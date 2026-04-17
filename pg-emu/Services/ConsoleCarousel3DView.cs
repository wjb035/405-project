using Godot;
using System.Collections.Generic;

namespace PGEmu.Services;

public partial class ConsoleCarousel3DView : SubViewportContainer
{
	private const ulong SettleSpinSuppressWindowMs = 90;
	private const string GbaScreenLogoPath = "res://Models/gba_logo2.png";
	private const string PspScreenLogoPath = "res://Models/psp_logo.png";
	private const float PspScreenNudgeLeftU = 0.220f;
	private const float GbaScreenUvScaleU = 3.074675f;
	private const float GbaScreenUvScaleV = -4.6753664f;
	private const float GbaScreenUvOffsetU = -1.825017f;
	private const float GbaScreenUvOffsetV = 3.91563f;
	private const double GbaOpen = 1.2;
	// Physics
	private const float HoverMotionThreshold = 0.001f;
	private float _velocity = 0f;
	private const float Friction = 3.5f;
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
	private readonly HashSet<Node3D> _openAnimatedConsoles = new();
	private readonly HashSet<Node3D> _manuallyClosedAnimatedConsoles = new();
	private readonly HashSet<Node3D> _pendingConsoleCloseSounds = new();
	private Texture2D? _gbaScreenLogoTexture;
	private Texture2D? _pspScreenLogoTexture;
	
	public event System.Action<int>? SelectionChanged;

	// Types of consoles we support
	public enum ConsoleType { Wii, PlayStation2, PSP, GameCube, GBA }
	
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
			LightEnergy = 1.2f,
			LightColor = new Color(0.95f, 0.90f, 1.0f),
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
			LightEnergy = 0.4f,
			LightColor = new Color(0.62f, 0.52f, 0.90f),
		};
		fill.RotateX(Mathf.DegToRad(20f));
		fill.RotateY(Mathf.DegToRad(-120f));
		_sceneRoot.AddChild(fill);
		
		var rim = new OmniLight3D
		{
			Position = new Vector3(0, 3f, -4f),
			LightEnergy = 1.2f,
			OmniRange = 15f,
			LightColor = new Color(0.70f, 0.60f, 1.0f), 
		};
		_sceneRoot.AddChild(rim);
		
		var env = new Environment();
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = new Color(0.3f, 0.2f, 0.5f);
		env.AmbientLightEnergy = 0.3f;

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
			AlbedoColor = new Color(0.08f, 0.05f, 0.15f, 0.5f),
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
				model.Scale = new Vector3(0.8f, 0.8f, 0.8f);
				model.Position = new Vector3(-0.1f, 0.1f, 0);
				model.RotateY(Mathf.DegToRad(-60f));
				break;
			case ConsoleType.PlayStation2:
				model.Scale = new Vector3(0.15f, 0.15f, 0.15f);
				model.Position = new Vector3(0.05f, -1f, 0);
				model.RotateY(Mathf.DegToRad(30f));
				break;
				case ConsoleType.PSP:
					var pspScale = ComputeUniformScaleToFit(model, 3.4f);
					model.Scale = new Vector3(pspScale, pspScale, pspScale);
					CenterNode3D(model);
						var pspPivot = new Node3D { Name = "PSPPivot" };
						pspPivot.Position = new Vector3(0.08f, -0.38f, 0f);
						pspPivot.RotateX(Mathf.DegToRad(10f));
						pspPivot.RotateY(Mathf.DegToRad(-170f));
						pspPivot.AddChild(model);
						modelRoot = pspPivot;
						break;
			case ConsoleType.GameCube:
				model.Scale = new Vector3(0.030f, 0.030f, 0.030f);
				model.Position = new Vector3(0, -0.7f, 0);
				model.RotateY(Mathf.DegToRad(30f));
				break;
			case ConsoleType.GBA:
				CenterNode3D(model);
				model.Scale = new Vector3(0.20f, 0.20f, 0.20f);
				model.Position = new Vector3(0.08f, -0.22f, 0f);
				model.RotateX(Mathf.DegToRad(14f));
				model.RotateY(Mathf.DegToRad(-18f));
				break;
		}
		wrapper.AddChild(modelRoot);
		ConfigureConsoleAnimation(wrapper, model, type);
		return wrapper;
		
	}
	
	//  Fallback for if the console doesnt have a model
	private Node3D BuildPlaceholder(ConsoleType type)
{
	var root = new Node3D();
	var mesh = new MeshInstance3D { Name = "Mesh" };

	switch (type)
	{
		case ConsoleType.Wii:
			mesh.Mesh = new BoxMesh { Size = new Vector3(0.8f, 3.2f, 0.4f) };
			mesh.SetSurfaceOverrideMaterial(0, MakeMat(new Color(0.92f, 0.92f, 0.90f)));
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

	private static void CenterNode3D(Node3D root)
	{
		if (!TryGetNodeBounds(root, Transform3D.Identity, out var bounds))
			return;

		root.Position -= bounds.GetCenter();
	}

	private static float ComputeUniformScaleToFit(Node3D root, float targetMaxDimension)
	{
		if (targetMaxDimension <= 0f ||
			!TryGetNodeBounds(root, Transform3D.Identity, out var bounds))
		{
			return 1f;
		}

		var maxDimension = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, bounds.Size.Z));
		if (maxDimension <= 0.0001f)
			return 1f;

		return targetMaxDimension / maxDimension;
	}

	private static bool TryGetNodeBounds(Node3D node, Transform3D accumulatedTransform, out Aabb bounds)
	{
		var hasBounds = false;
		bounds = new Aabb();
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
			if (child is not Node3D childNode || !TryGetNodeBounds(childNode, currentTransform, out var childBounds))
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

		ApplyGbaScreenLogoRecursive(node, screenTexture);
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

	private void ApplyGbaScreenLogoRecursive(Node node, Texture2D screenTexture)
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
			ApplyGbaScreenLogoRecursive((Node)child, screenTexture);
	}

	private void ApplyPspScreenLogo(Node node)
	{
		var screenTexture = GetPspScreenLogoTexture();
		if (screenTexture == null)
			return;

		ApplyPspScreenLogoRecursive(node, screenTexture, node.Name.ToString());
	}

	private Texture2D? GetPspScreenLogoTexture()
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

	private void ApplyPspScreenLogoRecursive(Node node, Texture2D screenTexture, string hierarchyHint)
	{
		var nodeName = node.Name.ToString();
		var currentHint = string.IsNullOrEmpty(hierarchyHint) ? nodeName : $"{hierarchyHint}/{nodeName}";

		if (node is MeshInstance3D mesh)
		{
			var meshName = mesh.Name.ToString();
			var isLikelyScreenMesh =
				meshName.Contains("screen", System.StringComparison.OrdinalIgnoreCase) ||
				meshName.Contains("phong2", System.StringComparison.OrdinalIgnoreCase) ||
				meshName.Contains("object_179", System.StringComparison.OrdinalIgnoreCase) ||
				meshName.Contains("pantalla", System.StringComparison.OrdinalIgnoreCase) ||
				currentHint.Contains("object_179", System.StringComparison.OrdinalIgnoreCase) ||
				currentHint.Contains("pantalla", System.StringComparison.OrdinalIgnoreCase) ||
				currentHint.Contains("3dsmeshmatrix55", System.StringComparison.OrdinalIgnoreCase);

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
			ApplyPspScreenLogoRecursive((Node)child, screenTexture, currentHint);
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
		animationPlayer.Seek(0.0, true);
		animationPlayer.Stop(true);

		_consoleAnimations[wrapper] = animationPlayer;
		_consoleAnimationNames[wrapper] = animationName;
		_consoleAnimationLengths[wrapper] = animation.Length;
		_consoleAnimationHoldTimes[wrapper] = type == ConsoleType.GBA
			? Mathf.Min((float)animation.Length, (float)GbaOpen)
			: animation.Length;
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

			var holdTime = _consoleAnimationHoldTimes.TryGetValue(box, out var configuredHoldTime)
				? configuredHoldTime
				: animationLength;

			if (_openAnimatedConsoles.Contains(box))
			{
				if (!animationPlayer.IsPlaying() ||
					animationPlayer.CurrentAnimationPosition >= holdTime - 0.02)
				{
					SetConsoleAnimationPose(box, holdTime, holdPose: true);
				}
				continue;
			}

			var isClosed = !animationPlayer.IsPlaying() ||
				animationPlayer.CurrentAnimationPosition <= 0.02;
			if (_pendingConsoleCloseSounds.Contains(box) && isClosed)
			{
				AudioManager.Instance?.PlayConsoleAnimationSfx(AudioManager.GbaCloseSfxPath);
				_pendingConsoleCloseSounds.Remove(box);
			}

			if (isClosed)
			{
				SetConsoleAnimationPose(box, 0.0, holdPose: false);
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

	private void OpenAnimatedConsole(Node3D box)
	{
		if (!_consoleAnimations.TryGetValue(box, out var animationPlayer) ||
			!_consoleAnimationNames.TryGetValue(box, out var animationName))
		{
			return;
		}

		_manuallyClosedAnimatedConsoles.Remove(box);
		_pendingConsoleCloseSounds.Remove(box);
		animationPlayer.Play(animationName);
		_openAnimatedConsoles.Add(box);
		if (IsConsoleType(box, ConsoleType.GBA))
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

		if (_consoleAnimationHoldTimes.TryGetValue(box, out var holdTime))
			animationPlayer.Seek(holdTime, true);
		animationPlayer.PlayBackwards(animationName);
		_openAnimatedConsoles.Remove(box);
		if (IsConsoleType(box, ConsoleType.GBA))
			_pendingConsoleCloseSounds.Add(box);
		else
			_pendingConsoleCloseSounds.Remove(box);
	}

	private static bool IsConsoleType(Node3D box, ConsoleType type)
	{
		return string.Equals(box.Name.ToString(), type.ToString(), System.StringComparison.Ordinal);
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
			new Vector3(SelectedScale * 1.02f, SelectedScale * 1.02f, SelectedScale * 1.02f),
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
			
			var brightness = Mathf.Lerp(1.0f, 0.5f, t);
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

		var currentStep = Mathf.RoundToInt(_spinAudioPos);
		if (currentStep == _lastSpinAudioStep)
			return;

		_lastSpinAudioStep = currentStep;
		PlaySpinAudio();
	}

	private void PlaySpinAudio(bool suppressIfRecent = false)
	{
		var now = Time.GetTicksMsec();
		if (suppressIfRecent && now - _lastSpinAudioMs < SettleSpinSuppressWindowMs)
			return;

		AudioManager.Instance?.PlayCarouselSpin();
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
