using Godot;
using System.Collections.Generic;

namespace PGEmu.Services;

public partial class GameCarousel3DView : SubViewportContainer
{
	private const ulong SettleSpinSuppressWindowMs = 90;
	private const string GbaCartridgeModelPath = "res://Models/gbaCart.glb";
	private const string GbaCartridgeLogoPath = "res://Models/gba_logo2.png";
	private const string DsCartridgeModelPath = "res://Models/ds_cart.glb";
	private const float DsCartridgeUvScaleU = 1.18f;
	private const float DsCartridgeUvOffsetU = -0.09f;
	private const float DsCartridgeUvScaleV = 1.0f;
	private const float DsCartridgeUvOffsetV = 0.0f;
	private const float GbaCartridgeUvScaleU = 1.4593054f;
	private const float GbaCartridgeUvOffsetU = -0.23596f;
	private const float GbaCartridgeUvScaleV = -2.8823564f;
	private const float GbaCartridgeUvOffsetV = 1.874445f;
	private const int GbaFrontLabelViewportWidth = 1024;
	private const int GbaFrontLabelViewportHeight = 608;
	private const int GbaBackLabelViewportWidth = 1280;
	private const int GbaBackLabelViewportHeight = 960;
	private const float GbaBackLabelWidthFactor = 0.94f;
	private const float GbaBackLabelHeightFactor = 0.78f;
	private const float GbaBackLabelYOffsetFactor = 0.02f;
	private const float GbaBackLabelDepthOffset = 0.016f;
	private static readonly Vector2I SquareBackViewportSize = new(1536, 1536);
	private static readonly Vector2I LandscapeBackViewportSize = new(2172, 1241);
	private static readonly Vector2I PortraitBackViewportSize = new(1536, 2172);
	private static readonly Vector3 N64BoxSize = new(3.6f, 3.6f * 4f / 7f, 0.25f);
	private static readonly Vector2 N64CoverSize = new(3.5f, 3.5f * 4f / 7f);
	private static readonly Vector2 GbaExpandedBackPanelSize = new(2.5f, 3.5f);
	private const float GbaExpandedBackDepthOffset = 0.03f;
	private const float VisibleCarouselDistance = 4.25f;
	private const float AlphaUpdateEpsilon = 0.003f;
	// Physics
	private const float HoverMotionThreshold = 0.001f;
	private float _velocity = 0f;
	private const float Friction = 3.5f;
	private const float DragScale = 0.004f;
	private const float FlingMultiplier = 15f; 
	private float _dragStartX;
	private float _dragStartPos;
	private bool _dragging;
	private float _lastDragX;
	private float _lastDragVelocity;

	// Carousel state (mirrors GameSelect._carouselPos)
	public float CarouselPos { get; private set; } = 0f;
	private int _count = 0;
	private float _spinAudioPos = 0f;
	private float _lastSpinAudioCarouselPos = 0f;
	private int _lastSpinAudioStep = 0;
	private ulong _lastSpinAudioMs;
	private float _spinSpeed = 0f;
	private const ulong SpinAudioCooldownMs = 70;
	private ulong _lastSpinTickMs = 0;

	// 3D scene internals
	private SubViewport _viewport;
	private Node3D _sceneRoot;
	private Camera3D _camera;
	private readonly List<Node3D> _boxes = new();
	private readonly List<List<StandardMaterial3D>> _baseMaterials = new();
	private readonly List<StandardMaterial3D?> _coverMaterials = new();
	private readonly List<float> _lastAppliedAlphas = new();
	private readonly List<int> _visibleBoxIndices = new();
	private readonly Dictionary<MeshInstance3D, SubViewport> _textureViewports = new();
	private bool _isGba;
	private bool _isDs;
	private bool _isPs1;
	private bool _isN64;
	private bool _isSnes;
	private Texture2D? _gbaCartridgeLogoTexture;
	private PackedScene? _gbaCartridgeScene;
	private PackedScene? _dsCartridgeScene;
	private bool _layoutDirty = true;
	private float _lastLaidOutCarouselPos = float.NaN;
	
	// Card spacing in 3D units
	private const float Spacing = 2.55f;
	private const float SelectedScale = 1.0f;
	private const float UnselectedScale = 0.72f;

	// Tweening animation
	private int _hoveredIdx = -1;
	private Tween? _hoverTween;
	private double _mouseIdleTime = 0f;
	private const double MouseIdleThreshold = 1.0; 
	private readonly HashSet<int> _flippedBoxes = new();
	private readonly HashSet<int> _animatingBoxes = new();
	private readonly HashSet<int> _expandedGbaBacks = new();
	
	// Passive animation
	private double _swayTime = 0;
	private float _currentSwayAngle = 0f;
	private const float SwayAmplitude = 0.04f;
	private const float SwaySpeed = 0.8f;
	private const float SwayBobAmplitude = 0.03f;
	private float _bobStrength = 1.0f; 
	private readonly HashSet<int> _returningBoxes = new();
	
	public event System.Action<int>? SelectionChanged;
	public override void _Ready()
	{
		// Builds the SubViewport, which basically renders a 3d sub scene in a 2d UI.
		_viewport = new SubViewport
		{
			Name = "Viewport3D",
			TransparentBg = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
		};
		AddChild(_viewport);
		Stretch = true;
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		SizeFlagsVertical = SizeFlags.ExpandFill;
		MouseFilter = MouseFilterEnum.Pass;
		
		// Scene root
		_sceneRoot = new Node3D { Name = "SceneRoot" };
		_viewport.AddChild(_sceneRoot);

		
		// Camera
		_camera = new Camera3D
		{
			Name = "Camera",
			Position = new Vector3(0, 0f, 2.5f),
			
		};
		_camera.Fov = 90f;
		_sceneRoot.AddChild(_camera);
		
		// Lighting
		var sun = new DirectionalLight3D
		{
			Position = new Vector3(2, 4, 3),
			LightEnergy = 1.8f, // 1.1f
			LightColor = new Color(0.85f, 0.80f, 1.0f),
		};
		sun.RotateX(Mathf.DegToRad(-45f));
		_sceneRoot.AddChild(sun);

		var ambient = new OmniLight3D
		{
			Position = new Vector3(0, 2, 4),
			LightEnergy = 0.8f, // 0.5f
			OmniRange = 20f,
			LightColor = new Color(0.62f, 0.38f, 1.0f),
		};
		_sceneRoot.AddChild(ambient);
		
		var rim = new OmniLight3D
		{
			Position = new Vector3(0, 1f, -3f),
			LightEnergy = 0.9f, //0.4f
			OmniRange = 12f,
			LightColor = new Color(0.60f, 0.82f, 1.0f), 
		};
		_sceneRoot.AddChild(rim);
		
		var fill = new OmniLight3D
		{
			Position = new Vector3(0, 2, 2),
			LightEnergy = 0.5f,
			LightColor = new Color(1f, 0.98f, 0.92f)
		};
		_sceneRoot.AddChild(fill);

		var env = new Environment();
	
		var sky = new Sky();
		sky.SkyMaterial = new ProceduralSkyMaterial();

		env.Sky = sky;
		env.BackgroundMode = Environment.BGMode.Sky;

		env.AmbientLightSource = Environment.AmbientSource.Sky;		
		env.AmbientLightEnergy = 0.55f;
		env.AmbientLightSkyContribution = 0.5f;
		

		var worldEnv = new WorldEnvironment { Environment = env };
		_sceneRoot.AddChild(worldEnv);
		
		// Aliasing
		_viewport.Msaa3D = Viewport.Msaa.Msaa8X;
		_viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
		_viewport.Scaling3DMode = SubViewport.Scaling3DModeEnum.Fsr;
		_viewport.Scaling3DScale = 1.0f;
		_viewport.FsrSharpness = 0.2f;
		
	}


	// Call this from GameSelect after loading games to clear everything and rebuild
	public void Populate(
		List<(string title, Texture2D? coverArt, string awardType)> games,
		float initialPos,
		bool isGba = false,
		bool isDs = false,
		bool isPs1 = false,
		bool isN64 = false,
		bool isSnes = false,
		IReadOnlyList<string?>? platformIds = null)
	{
		// Clear old boxes
		ClearTextureViewports();
		_hoverTween?.Kill();
		_hoverTween = null;
		foreach (var b in _boxes)
			b.QueueFree();
		_boxes.Clear();
		_baseMaterials.Clear();
		_coverMaterials.Clear();
		_lastAppliedAlphas.Clear();
		_visibleBoxIndices.Clear();
		_flippedBoxes.Clear();
		_animatingBoxes.Clear();
		_expandedGbaBacks.Clear();
		_returningBoxes.Clear();

		_count = games.Count;
		_isGba = isGba;
		_isDs = isDs;
		_isPs1 = isPs1;
		_isN64 = isN64;
		_isSnes = isSnes;
		
		CarouselPos = WrapPos(initialPos);
		_spinAudioPos = CarouselPos;
		_lastSpinAudioCarouselPos = CarouselPos;
		_lastSpinAudioStep = Mathf.RoundToInt(_spinAudioPos);
		_lastSpinAudioMs = 0;

		for (int index = 0; index < games.Count; index++)
		{
			var (title, coverArt, award) = games[index];
			var platformId = platformIds != null && index < platformIds.Count ? platformIds[index] : null;
			var square = !_isGba && !_isDs && SquarePlatform(platformId);
			var n64 = !_isGba && !_isDs && (_isN64 || N64Platform(platformId));
			var sideways = !_isGba && !_isDs && SidewaysPlatform(platformId);
			Node3D box;
			if (_isGba)
				box = BuildGbaCartridge(title, coverArt );
			else if (_isDs)
				box = BuildDsCartridge(title, coverArt);
			else
				box = BuildBox(title, coverArt, award, square, sideways, n64);
			_sceneRoot.AddChild(box);
			_boxes.Add(box);
			_lastAppliedAlphas.Add(float.NaN);
		}

		MarkLayoutDirty();
		LayoutBoxes();
	}
	
	// Builds actual 3d geometry of the cases
	private Node3D BuildBox(string title, Texture2D? coverArt, string awardType, bool square = false, bool sideways = false, bool n64 = false)
	{
		var root = new Node3D();
		var n64Box = n64;
		var squareBox = !n64Box && (_isPs1 || square);
		var sidewaysBox = _isSnes || sideways;
		root.SetMeta("pgemu_square_box", squareBox);
		root.SetMeta("pgemu_landscape_box", n64Box || sidewaysBox);
		var boxSize = squareBox
			? new Vector3(2.8f, 2.8f, 0.25f)
			: n64Box
				? N64BoxSize
				: sidewaysBox
				? new Vector3(3.6f, 2.6f, 0.25f)
				: new Vector3(2.6f, 3.6f, 0.25f);
		var coverSize = squareBox
			? new Vector2(2.68f, 2.68f)
			: n64Box
				? N64CoverSize
				: sidewaysBox
				? new Vector2(3.5f, 2.5f)
				: new Vector2(2.5f, 3.5f);

		// Box mesh 
		var mesh = new MeshInstance3D { Name = "Mesh" };
		var boxMesh = new BoxMesh
		{
			Size = boxSize
		};
		mesh.Mesh = boxMesh;

		// Material — dark base + cover art on the front
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.08f, 0.06f, 0.14f),
			RoughnessTexture = null,
			Roughness = 0.85f,
			Metallic = 0.05f,
		};
		if (awardType == "Mastery/Completion"){
			mat = CreateAwardMaterial(new Color(1.0f, 0.733f, 0.336f), 0.08f);
		}
		else if (awardType == "Game Beaten"){
			mat = CreateAwardMaterial(new Color(0.9f, 0.9f, 0.9f), 0.05f);
		}
		else{
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			// mat = gold;
		}
		
		_baseMaterials.Add(new List<StandardMaterial3D> { mat });
		
		mesh.SetSurfaceOverrideMaterial(0, mat);
		root.AddChild(mesh);
		
		// Cover art as a separate quad on the front face
		var coverMesh = new MeshInstance3D { Name = "CoverMesh" };
		var quad = new QuadMesh
		{
			Size = coverSize
		};
		coverMesh.Mesh = quad;
		coverMesh.Position = new Vector3(0, 0, 0.126f);
		
		if (coverArt != null)
		{
			// Front face mat with cover art
			var coverMat = new StandardMaterial3D
			{
				AlbedoTexture = coverArt,
				AlbedoColor = coverArt != null ? new Color(1,1,1,1) : new Color(1,1,1,0),
				Roughness = 0.5f,
				Metallic = 0.1f,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
			};
			
			coverMesh.SetSurfaceOverrideMaterial(0, coverMat);
			_coverMaterials.Add(coverMat);
		}
		else
		{
			// placeholder for if nothing loads
			var placeholderMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.75f, 0.72f, 0.85f, 1f), 
				Roughness = 0.85f,
				Metallic = 0.05f,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
			};
			coverMesh.SetSurfaceOverrideMaterial(0, placeholderMat);
			_coverMaterials.Add(placeholderMat);
		}
		root.AddChild(coverMesh);
		
		// Back face label mesh and quad
		var backMesh = new MeshInstance3D { Name = "BackMesh" };
		var backQuad = new QuadMesh
		{
			Size = coverSize
		};
		
		backMesh.Mesh = backQuad;
		backMesh.Position = new Vector3(0, 0, -0.126f);
		
		// Flip it so it faces outwards
		backMesh.RotateY(Mathf.DegToRad(180f));
		
		var backMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 1, 1, 1),
			Roughness = 0.85f,
			Metallic = 0.05f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
		};
		backMesh.SetSurfaceOverrideMaterial(0, backMat);
		root.AddChild(backMesh);
		
		
		
		// Side mesh for the progress bar
		var sideMesh = new MeshInstance3D { Name = "SideMesh" };
		var sideQuad = new QuadMesh
		{
			Size = new Vector2(0.18f, 0.8f)
		};
		
		sideMesh.Mesh = sideQuad;
		sideMesh.Position = new Vector3(-(boxSize.X / 2f) - 0.001f, 0, 0f);
		
		// Flip it so it faces outwards
		sideMesh.RotateY(Mathf.DegToRad(-90f));
		
		var sideMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0, 0, 0, 0),
			Roughness = 0.5f,
			Metallic = 0.05f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
		};
		sideMesh.SetSurfaceOverrideMaterial(0, sideMat);
		root.AddChild(sideMesh);
		
		return root;
		
	}

	private static StandardMaterial3D CreateAwardMaterial(Color color, float roughness)
	{
		return new StandardMaterial3D
		{
			AlbedoColor = color,
			Metallic = 1.0f,
			Roughness = roughness,
		};
	}

	private Node3D BuildGbaCartridge(string title, Texture2D? coverArt)
	{
		var scene = LoadPackedScene(GbaCartridgeModelPath, ref _gbaCartridgeScene);
		if (scene == null)
		{
			GD.PrintErr($"GBA cartridge model not found at {GbaCartridgeModelPath}, falling back to box geometry");
			return BuildBox(title, coverArt, "none");
		}

		var model = scene.Instantiate<Node3D>();
		CenterNode3D(model);
		model.Scale = new Vector3(2.8f, 2.8f, 2.8f);
		model.RotateY(Mathf.DegToRad(-90f));
		var hasBounds = TryGetNodeBounds(model, Transform3D.Identity, out var modelBounds);

		var root = new Node3D();
		root.SetMeta("pgemu_title", title);
		root.AddChild(model);

		var materials = new List<StandardMaterial3D>();
		MeshInstance3D? labelMesh = null;
		CollectImportedMaterials(model, materials, ref labelMesh);
		_baseMaterials.Add(materials);
		labelMesh ??= FindFirstMeshInstance(model);

		if (labelMesh != null)
		{
			labelMesh.Name = "CoverMesh";
			var template = labelMesh.GetSurfaceOverrideMaterial(0) as StandardMaterial3D;
			var coverMat = ApplyGbaLabelTexture(labelMesh, title, coverArt, template);
			_coverMaterials.Add(coverMat);
		}
		else
		{
			GD.PrintErr($"Could not locate a label mesh for GBA cartridge: {title}");
			_coverMaterials.Add(null);
		}

		var backMesh = new MeshInstance3D { Name = "BackMesh" };
		var backLabelSize = hasBounds
			? new Vector2(modelBounds.Size.X * GbaBackLabelWidthFactor, modelBounds.Size.Y * GbaBackLabelHeightFactor)
			: new Vector2(2.15f, 1.55f);
		backMesh.Mesh = new QuadMesh { Size = backLabelSize };

		var backLabelPosition = hasBounds
			? new Vector3(
				modelBounds.GetCenter().X,
				modelBounds.GetCenter().Y + modelBounds.Size.Y * GbaBackLabelYOffsetFactor,
				modelBounds.Position.Z - GbaBackLabelDepthOffset)
			: new Vector3(0f, -0.12f, -0.32f);
		backMesh.Position = backLabelPosition;
		backMesh.RotateY(Mathf.DegToRad(180f));

		var backMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 1, 1, 1),
			Roughness = 0.5f,
			Metallic = 0.05f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
		};
		backMesh.SetSurfaceOverrideMaterial(0, backMat);
		root.AddChild(backMesh);

		var detailBackMesh = new MeshInstance3D
		{
			Name = "DetailBackMesh",
			Visible = false,
		};
		detailBackMesh.Mesh = new QuadMesh { Size = GbaExpandedBackPanelSize };
		detailBackMesh.Position = hasBounds
			? new Vector3(
				modelBounds.GetCenter().X,
				modelBounds.GetCenter().Y,
				modelBounds.Position.Z - GbaExpandedBackDepthOffset)
			: new Vector3(0f, 0f, -0.36f);
		detailBackMesh.RotateY(Mathf.DegToRad(180f));
		var detailBackMat = new StandardMaterial3D
		{
			AlbedoColor = Colors.White,
			Roughness = 0.9f,
			Metallic = 0f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
		};
		detailBackMesh.SetSurfaceOverrideMaterial(0, detailBackMat);
		root.AddChild(detailBackMesh);

		return root;
	}

	private Node3D BuildDsCartridge(string title, Texture2D? coverArt)
	{
		var scene = LoadPackedScene(DsCartridgeModelPath, ref _dsCartridgeScene);
		if (scene == null)
		{
			GD.PrintErr($"DS cartridge model not found at {DsCartridgeModelPath}, falling back to box geometry");
			return BuildBox(title, coverArt, "none");
		}

		var model = scene.Instantiate<Node3D>();
		CenterNode3D(model);
		model.Scale = new Vector3(20f, 20f, 20f);
		model.RotateX(Mathf.DegToRad(90f));
		CenterNode3D(model);
		var hasBounds = TryGetNodeBounds(model, Transform3D.Identity, out var modelBounds);

		var root = new Node3D();
		root.SetMeta("pgemu_title", title);
		root.AddChild(model);

		var materials = new List<StandardMaterial3D>();
		MeshInstance3D? labelMesh = null;
		CollectImportedMaterials(model, materials, ref labelMesh);
		_baseMaterials.Add(materials);
		labelMesh = FindMeshInstanceByDescriptor(model, "Sketchfab_Scene_Object_4")
			?? FindMeshInstanceByDescriptor(model, "Object_4")
			?? labelMesh;

		if (labelMesh != null)
		{
			labelMesh.Name = "CoverMesh";
			var template = labelMesh.GetSurfaceOverrideMaterial(0) as StandardMaterial3D;
			var coverMat = ApplyDsLabelTexture(labelMesh, coverArt, template);
			_coverMaterials.Add(coverMat);
		}
		else
		{
			GD.PrintErr($"Could not locate a label mesh for DS cartridge: {title}");
			_coverMaterials.Add(null);
		}

		var backMesh = new MeshInstance3D { Name = "BackMesh" };
		var backLabelSize = hasBounds
			? new Vector2(modelBounds.Size.X * 0.82f, modelBounds.Size.Y * 0.86f)
			: new Vector2(1.55f, 1.7f);
		backMesh.Mesh = new QuadMesh { Size = backLabelSize };
		backMesh.Position = hasBounds
			? new Vector3(
				modelBounds.GetCenter().X,
				modelBounds.GetCenter().Y,
				modelBounds.Position.Z - 0.018f)
			: new Vector3(0f, 0f, -0.12f);
		backMesh.RotateY(Mathf.DegToRad(180f));

		var backMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 1, 1, 1),
			Roughness = 0.65f,
			Metallic = 0.02f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
		};
		backMesh.SetSurfaceOverrideMaterial(0, backMat);
		root.AddChild(backMesh);

		return root;
	}

	// Updates a box's texture,c all it from gameselect
	public void UpdateCoverArt(int index, Texture2D texture)
	{
		if (index >= _boxes.Count) return;
		var coverMesh = FindMeshByName(_boxes[index], "CoverMesh");
		if (coverMesh == null) return;

		var previousMaterial = index < _coverMaterials.Count ? _coverMaterials[index] : null;
		var coverMat = _isGba
			? ApplyGbaLabelTexture(
				coverMesh,
				_boxes[index].HasMeta("pgemu_title") ? _boxes[index].GetMeta("pgemu_title").AsString() : string.Empty,
				texture,
				previousMaterial)
			: _isDs
				? ApplyDsLabelTexture(coverMesh, texture, previousMaterial)
				: ApplyCoverTexture(coverMesh, texture, previousMaterial);

		if (index < _coverMaterials.Count)
			_coverMaterials[index] = coverMat;

		if (index < _baseMaterials.Count && previousMaterial != null)
		{
			var baseMaterials = _baseMaterials[index];
			var previousIdx = baseMaterials.IndexOf(previousMaterial);
			if (previousIdx >= 0)
				baseMaterials[previousIdx] = coverMat;
		}

		InvalidateMaterialAlpha(index);
	}
	
	// Build the texture on the back
	private void BuildBackFaceTexture(int index, string title, string description, string genre, string releaseYear, string rating)
	{
		var box = _boxes[index];
		var backMesh = FindMeshByName(box, "BackMesh");
		if (backMesh == null) return;

		if (_isGba)
		{
			BuildGbaCompactBackFaceTexture(backMesh, title, genre, releaseYear, rating);

			var detailBackMesh = FindMeshByName(box, "DetailBackMesh");
			if (detailBackMesh != null)
				BuildBoxStyleBackFaceTexture(detailBackMesh, title, description, genre, releaseYear, rating);
			return;
		}

		BuildBoxStyleBackFaceTexture(backMesh, title, description, genre, releaseYear, rating);
	}

	private void BuildBoxStyleBackFaceTexture(MeshInstance3D backMesh, string title, string description,
		string genre, string releaseYear, string rating)
	{
		var squareBox = SquareBox(backMesh);
		var landscapeBox = LandscapeBox(backMesh);
		var vpSize = squareBox
			? SquareBackViewportSize
			: landscapeBox
				? LandscapeBackViewportSize
				: PortraitBackViewportSize;
		var vp = AddTextureViewport(backMesh, vpSize, SubViewport.UpdateMode.Once, Viewport.Msaa.Msaa8X,
			Viewport.ScreenSpaceAAEnum.Fxaa);
		
		// Root panel
		var panel = new PanelContainer();
		var style = new StyleBoxFlat()
		{
			BgColor = new Color(0.06f, 0.04f, 0.12f, 0.97f),
			BorderColor = new Color(0.55f, 0.42f, 0.80f, 0.8f),
			BorderWidthLeft = 12,
			BorderWidthTop = 12,
			BorderWidthRight = 12,
			BorderWidthBottom = 12,
			CornerRadiusTopLeft = 72,
			CornerRadiusTopRight = 72,
			CornerRadiusBottomLeft = 72,
			CornerRadiusBottomRight = 72,
			ContentMarginLeft = 120,
			ContentMarginTop = 120,
			ContentMarginRight = 120,
			ContentMarginBottom = 120,
		};
		panel.AddThemeStyleboxOverride("panel", style);
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		vp.AddChild(panel);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 40);
		panel.AddChild(column);

		// Title
		var titleLabel = new Label
		{
			Text = title,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		StyleLabel(titleLabel, new Color(0.97f, 0.95f, 1f, 1f), 96);
		column.AddChild(titleLabel);

		// Divider
		var divider = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, 6),
			Color = new Color(0.55f, 0.42f, 0.80f, 0.6f),
		};
		column.AddChild(divider);
		
		// Metadata
		AddMetaRow(column, "GENRE", genre);
		AddMetaRow(column, "RELEASED", releaseYear);
		AddMetaRow(column, "RATING", rating);
		
		// Description
		if (!string.IsNullOrWhiteSpace(description))
		{
			var spacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
			column.AddChild(spacer);

			var desc = new Label
			{
				Text = description,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			StyleLabel(desc, new Color(0.82f, 0.80f, 0.94f, 0.90f), 58);
			column.AddChild(desc);
		}
		
		// Apply this vp to the back face
		var mat = backMesh.GetSurfaceOverrideMaterial(0) as StandardMaterial3D;
		if (mat != null)
		{
			mat.AlbedoTexture = vp.GetTexture();
			mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		}
	}

	private void BuildGbaCompactBackFaceTexture(MeshInstance3D backMesh, string title,
		string genre, string releaseYear, string rating)
	{
		var vp = AddTextureViewport(
			backMesh,
			new Vector2I(GbaBackLabelViewportWidth, GbaBackLabelViewportHeight),
			SubViewport.UpdateMode.Once,
			Viewport.Msaa.Disabled,
			Viewport.ScreenSpaceAAEnum.Disabled);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		vp.AddChild(margin);

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.985f, 0.985f, 0.965f, 1f),
			BorderColor = new Color(0.06f, 0.06f, 0.07f, 1f),
			BorderWidthLeft = 8,
			BorderWidthTop = 8,
			BorderWidthRight = 8,
			BorderWidthBottom = 8,
			CornerRadiusTopLeft = 22,
			CornerRadiusTopRight = 22,
			CornerRadiusBottomLeft = 22,
			CornerRadiusBottomRight = 22,
			ContentMarginLeft = 34,
			ContentMarginTop = 28,
			ContentMarginRight = 34,
			ContentMarginBottom = 24,
		};
		panel.AddThemeStyleboxOverride("panel", style);
		margin.AddChild(panel);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 14);
		panel.AddChild(column);

		var titleLabel = new Label
		{
			Text = BuildCompactGbaTitle(title),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		StyleLabel(titleLabel, new Color(0.04f, 0.04f, 0.05f, 1f), 168);
		column.AddChild(titleLabel);

		var divider = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, 6),
			Color = new Color(0.08f, 0.08f, 0.09f, 0.75f),
		};
		column.AddChild(divider);

		var metaLabel = new Label
		{
			Text = BuildCompactGbaMetaLine(genre, releaseYear, rating),
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		StyleLabel(metaLabel, new Color(0.16f, 0.16f, 0.18f, 0.88f), 104);
		column.AddChild(metaLabel);

		var hintLabel = new Label
		{
			Text = "Double-click for details",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		StyleLabel(hintLabel, new Color(0.24f, 0.24f, 0.26f, 0.80f), 56);
		column.AddChild(hintLabel);

		var mat = backMesh.GetSurfaceOverrideMaterial(0) as StandardMaterial3D;
		if (mat != null)
		{
			mat.AlbedoTexture = vp.GetTexture();
			mat.AlbedoColor = Colors.White;
			mat.Roughness = 0.98f;
			mat.Metallic = 0f;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
		}
	}

	// Function for adding metadata
	private static void AddMetaRow(VBoxContainer parent, string label, string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return;

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 40);
		parent.AddChild(row);
		
		var keyLabel = new Label { Text = label };
		StyleLabel(keyLabel, new Color(0.62f, 0.74f, 0.94f, 0.88f), 52);
		keyLabel.CustomMinimumSize = new Vector2(380, 0);
		row.AddChild(keyLabel);

		var valueLabel = new Label
		{
			Text = value,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		StyleLabel(valueLabel, new Color(0.95f, 0.93f, 1f, 0.96f), 52);
		row.AddChild(valueLabel);
		
	}

	private static void AddGbaMetaPair(GridContainer parent, string label, string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return;

		var keyLabel = new Label { Text = label };
		StyleLabel(keyLabel, new Color(0.24f, 0.24f, 0.26f, 0.88f), 30);
		parent.AddChild(keyLabel);

		var valueLabel = new Label
		{
			Text = value,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		StyleLabel(valueLabel, new Color(0.08f, 0.08f, 0.10f, 0.98f), 30);
		parent.AddChild(valueLabel);
	}

	private static void AddGbaMetaChip(HBoxContainer parent, string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;

		var chip = new PanelContainer();
		var chipStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.12f, 0.14f, 1f),
			CornerRadiusTopLeft = 18,
			CornerRadiusTopRight = 18,
			CornerRadiusBottomLeft = 18,
			CornerRadiusBottomRight = 18,
			ContentMarginLeft = 18,
			ContentMarginTop = 10,
			ContentMarginRight = 18,
			ContentMarginBottom = 10,
		};
		chip.AddThemeStyleboxOverride("panel", chipStyle);
		parent.AddChild(chip);

		var label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		StyleLabel(label, new Color(0.99f, 0.99f, 1f, 1f), 42);
		chip.AddChild(label);
	}

	private SubViewport AddTextureViewport(MeshInstance3D owner, Vector2I size, SubViewport.UpdateMode updateMode,
		Viewport.Msaa msaa, Viewport.ScreenSpaceAAEnum screenAa = Viewport.ScreenSpaceAAEnum.Disabled)
	{
		if (_textureViewports.TryGetValue(owner, out var oldViewport) &&
			GodotObject.IsInstanceValid(oldViewport))
		{
			oldViewport.QueueFree();
		}

		var vp = new SubViewport
		{
			Size = size,
			TransparentBg = true,
			RenderTargetUpdateMode = updateMode,
			Msaa2D = msaa,
			ScreenSpaceAA = screenAa,
		};
		_sceneRoot.AddChild(vp);
		_textureViewports[owner] = vp;
		return vp;
	}

	private void ClearTextureViewports()
	{
		foreach (var viewport in _textureViewports.Values)
		{
			if (GodotObject.IsInstanceValid(viewport))
				viewport.QueueFree();
		}

		_textureViewports.Clear();
	}

	private static void StyleLabel(Label label, Color color, int fontSize)
	{
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeFontSizeOverride("font_size", fontSize);
	}

	private static string BuildCompactGbaMetaLine(string genre, string releaseYear, string rating)
	{
		var parts = new List<string>();

		if (!string.IsNullOrWhiteSpace(genre))
			parts.Add(AbbreviateCompactGbaGenre(genre));
		parts.Add(string.IsNullOrWhiteSpace(releaseYear) ? "YEAR TBD" : releaseYear);
		parts.Add(string.IsNullOrWhiteSpace(rating) ? "ACH 0/0" : $"ACH {rating}");

		return string.Join(" • ", parts);
	}

	private static string BuildCompactGbaTitle(string title)
	{
		if (string.IsNullOrWhiteSpace(title))
			return string.Empty;

		var parentheticalIndex = title.IndexOf(" (", System.StringComparison.Ordinal);
		return parentheticalIndex > 0 ? title[..parentheticalIndex].TrimEnd() : title.Trim();
	}

	private static string AbbreviateCompactGbaGenre(string genre)
	{
		if (string.Equals(genre.Trim(), "Game Boy Advance", System.StringComparison.OrdinalIgnoreCase))
			return "GBA";

		return genre.Trim();
	}

	private static string CompactGbaDescription(string description)
	{
		if (string.IsNullOrWhiteSpace(description) || description == "No description yet.")
			return string.Empty;

		var compact = description.Replace('\n', ' ').Replace('\r', ' ').Trim();
		if (compact.Length <= 110)
			return compact;

		return $"{compact[..107].TrimEnd()}...";
	}

	private StandardMaterial3D ApplyGbaLabelTexture(MeshInstance3D coverMesh, string title, Texture2D? coverTexture,
		StandardMaterial3D? template)
	{
		var labelTexture = BuildGbaFrontLabelTexture(coverMesh, title, coverTexture) ??
			coverTexture ??
			GetGbaCartridgeLogoTexture();

		var coverMat = template != null
			? (StandardMaterial3D)template.Duplicate()
			: new StandardMaterial3D();

		if (labelTexture != null)
			coverMat.AlbedoTexture = labelTexture;

		coverMat.AlbedoColor = Colors.White;
		coverMat.Roughness = 0.98f;
		coverMat.Metallic = 0f;
		coverMat.Transparency = template?.Transparency ?? BaseMaterial3D.TransparencyEnum.Disabled;
		coverMat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		coverMat.ResourceLocalToScene = true;
		coverMat.Uv1Scale = new Vector3(GbaCartridgeUvScaleU, GbaCartridgeUvScaleV, 1f);
		coverMat.Uv1Offset = new Vector3(GbaCartridgeUvOffsetU, GbaCartridgeUvOffsetV, 0f);
		coverMesh.SetSurfaceOverrideMaterial(0, coverMat);
		return coverMat;
	}

	private Texture2D? BuildGbaFrontLabelTexture(MeshInstance3D coverMesh, string title, Texture2D? coverTexture)
	{
		var displayTexture = coverTexture ?? GetGbaCartridgeLogoTexture();
		if (displayTexture == null)
			return null;

		var vp = AddTextureViewport(
			coverMesh,
			new Vector2I(GbaFrontLabelViewportWidth, GbaFrontLabelViewportHeight),
			SubViewport.UpdateMode.Once,
			Viewport.Msaa.Msaa8X,
			Viewport.ScreenSpaceAAEnum.Fxaa);

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var shellStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.94f, 0.93f, 0.96f, 1f),
			BorderColor = new Color(0.12f, 0.12f, 0.13f, 0.92f),
			BorderWidthLeft = 8,
			BorderWidthTop = 8,
			BorderWidthRight = 8,
			BorderWidthBottom = 8,
			CornerRadiusTopLeft = 28,
			CornerRadiusTopRight = 28,
			CornerRadiusBottomLeft = 28,
			CornerRadiusBottomRight = 28,
			ContentMarginLeft = 24,
			ContentMarginTop = 24,
			ContentMarginRight = 24,
			ContentMarginBottom = 24,
		};
		panel.AddThemeStyleboxOverride("panel", shellStyle);
		vp.AddChild(panel);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 18);
		panel.AddChild(column);

		var artFrame = new PanelContainer
		{
			CustomMinimumSize = new Vector2(0, 470),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		var artFrameStyle = new StyleBoxFlat
		{
			BgColor = coverTexture != null
				? new Color(0.09f, 0.09f, 0.11f, 1f)
				: new Color(0f, 0f, 0f, 1f),
			CornerRadiusTopLeft = 18,
			CornerRadiusTopRight = 18,
			CornerRadiusBottomLeft = 18,
			CornerRadiusBottomRight = 18,
			ContentMarginLeft = 16,
			ContentMarginTop = 16,
			ContentMarginRight = 16,
			ContentMarginBottom = 16,
		};
		artFrame.AddThemeStyleboxOverride("panel", artFrameStyle);
		column.AddChild(artFrame);

		var art = new TextureRect
		{
			Texture = displayTexture,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = coverTexture != null
				? TextureRect.StretchModeEnum.KeepAspectCovered
				: TextureRect.StretchModeEnum.KeepAspectCentered,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		artFrame.AddChild(art);

		var footer = new PanelContainer();
		var footerStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.16f, 0.13f, 0.24f, 0.98f),
			CornerRadiusTopLeft = 16,
			CornerRadiusTopRight = 16,
			CornerRadiusBottomLeft = 16,
			CornerRadiusBottomRight = 16,
			ContentMarginLeft = 20,
			ContentMarginTop = 12,
			ContentMarginRight = 20,
			ContentMarginBottom = 12,
		};
		footer.AddThemeStyleboxOverride("panel", footerStyle);
		column.AddChild(footer);

		var titleLabel = new Label
		{
			Text = title,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		StyleLabel(titleLabel, new Color(0.98f, 0.97f, 1f, 1f), 42);
		footer.AddChild(titleLabel);

		return vp.GetTexture();
	}

	// Public method for setting the data with IGDB api
	public void SetBackFaceData(int index, string title, string description,
		string genre, string releaseYear, string rating)
	{
		if (index >= _boxes.Count) return;
		BuildBackFaceTexture(index, title, description, genre, releaseYear, rating);
	}

	// public method for setting side data
	public void SetSideProgressBar(int index, ProgressBar prog){
		// GD.Print(index + " has a valid progress bar");
		
		if (index >= _boxes.Count) return;
		var sideMesh = FindMeshByName(_boxes[index], "SideMesh");
		if (sideMesh == null) return;

		var vp = AddTextureViewport(sideMesh, new Vector2I(64, 512), SubViewport.UpdateMode.Once, Viewport.Msaa.Msaa4X);
		
		
		prog.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		prog.FillMode = (int)ProgressBar.FillModeEnum.BottomToTop; 
		prog.ShowPercentage = true;
		vp.AddChild(prog);
		
		var mat = sideMesh.GetSurfaceOverrideMaterial(0) as StandardMaterial3D;
		if (mat != null)
		{
			mat.AlbedoTexture = vp.GetTexture();
			mat.AlbedoColor = Colors.White;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;

		}

	}


	// Physics processing for the spin
	public override void _Process(double delta)
	{
		_mouseIdleTime += delta;
		var wasMoving = IsCarouselMoving();

		if (wasMoving && _hoveredIdx != -1)
		{
			OnBoxHoverExit(_hoveredIdx);
			_hoveredIdx = -1;
		}
		
		if (!_dragging && Mathf.Abs(_velocity) > 0.001f)
		{
			CarouselPos += _velocity * (float)delta;
			CarouselPos = WrapPos(CarouselPos);
			MarkLayoutDirty();
			UpdateSpinAudioFromMotion();
			_velocity = Mathf.Lerp(_velocity, 0f, Friction * (float)delta);

			// Snap when nearly stopped
			if (Mathf.Abs(_velocity) < 0.05f)
			{
				_velocity = 0f;
				var nearest = Mathf.Round(CarouselPos);
				CarouselPos = WrapPos(nearest);
				MarkLayoutDirty();
				SelectionChanged?.Invoke(WrapIndex(Mathf.RoundToInt(CarouselPos)));
				UnflipSelected();
				ElasticSnapSelected();
				PlaySpinAudio(suppressIfRecent: true);
			}
		}

		// Idle spin on selected cartridge
		var selectedIdx = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (selectedIdx >= 0 &&
			selectedIdx < _boxes.Count &&
			!_dragging &&
			_mouseIdleTime > MouseIdleThreshold &&
			!_flippedBoxes.Contains(selectedIdx) &&
			_boxes[selectedIdx].Visible)
		{
			_boxes[selectedIdx].RotateY((float)delta * 0.4f);
		}

		if (_layoutDirty || !Mathf.IsEqualApprox(CarouselPos, _lastLaidOutCarouselPos))
			LayoutBoxes();

		ApplyPassiveSway(delta, wasMoving);
	}

	public override void _ExitTree()
	{
		AudioManager.Instance?.StopCarouselHover(false);
	}
	
	
	// Handle when the mouse is clicked or draggged, kills velocity so it doesnt drift when you drag
	public override void _GuiInput(InputEvent e)
	{
		if (InputRoutingService.Instance?.IsUiInputBlocked == true)
		{
			AcceptEvent();
			return;
		}
		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				if (mb.Position.Y < 70f)
					return;

				if (mb.DoubleClick && TryToggleSelectedGbaBackDetail(mb.Position))
				{
					AcceptEvent();
					return;
				}

				MouseFilter = MouseFilterEnum.Stop;
				_dragging = true;
				AudioManager.Instance?.StopCarouselHover();
				_dragStartX = mb.Position.X;
				_dragStartPos = CarouselPos;
				_lastDragX = mb.Position.X;
				_lastDragVelocity = 0f;
				_velocity = 0f;
			}
			else if (_dragging)
			{
				_dragging = false;
				MouseFilter = MouseFilterEnum.Pass;
				// Transfer drag velocity to physics
				_velocity = _lastDragVelocity;
			}
		}

		if (_dragging && e is InputEventMouseMotion mm)
		{
			var dx = mm.Position.X - _dragStartX;
			
			if (Mathf.Abs(dx) > 8f)
				UnflipSelected();
			
			CarouselPos = WrapPos(_dragStartPos - dx * DragScale * 5f);
			MarkLayoutDirty();
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
		if (InputRoutingService.Instance?.IsUiInputBlocked == true)
			return;
		if (e is InputEventKey) return;
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
	private void OnBoxHoverEnter(int index, Vector2 mousePos)
	{
		if (index >= _boxes.Count) return;
		var box = _boxes[index];

		if (!_dragging)
			AudioManager.Instance?.PlayCarouselHover();

		_hoverTween?.Kill();
		_hoverTween = CreateTween();
		_hoverTween.SetTrans(Tween.TransitionType.Back);
		_hoverTween.SetEase(Tween.EaseType.Out);
		
		_hoverTween.TweenProperty(box, "rotation",
			new Vector3(0f, _flippedBoxes.Contains(index) ? Mathf.DegToRad(180f) : 0f, 0f),
			0.15f);

		_hoverTween.TweenProperty(box, "scale",
			new Vector3(SelectedScale * 1.08f, SelectedScale * 1.08f, SelectedScale * 1.08f),
			0.2f);
	}

	private void OnBoxHoverExit(int index)
	{
		if (index >= _boxes.Count) return;
		var box = _boxes[index];

		AudioManager.Instance?.StopCarouselHover();

		_hoverTween?.Kill();
		_hoverTween = CreateTween();
		_hoverTween.SetParallel();
		_hoverTween.SetTrans(Tween.TransitionType.Spring);
		_hoverTween.SetEase(Tween.EaseType.Out);
		
		_returningBoxes.Add(index);
		_animatingBoxes.Add(index);
		
		
		// Reset scale
		_hoverTween.TweenProperty(box, "scale",
			new Vector3(SelectedScale, SelectedScale, SelectedScale),
			0.6f);
		
		// Reset tilt
		if (!_flippedBoxes.Contains(index))
		{
			_hoverTween.TweenProperty(box, "rotation",
				new Vector3(_currentSwayAngle, 0f, _currentSwayAngle * 0.5f), 0.4f);
		}
		else
		{
			_hoverTween.TweenProperty(box, "rotation",
				new Vector3(_currentSwayAngle, box.Rotation.Y, _currentSwayAngle * 0.5f), 0.4f);
		}
		
		var capturedIdx = index;
		_hoverTween.TweenCallback(Callable.From(() =>
		{
			_returningBoxes.Remove(capturedIdx);
			_animatingBoxes.Remove(capturedIdx);
		}));
	}

	private void UpdateHoverTilt(int index, Vector2 mousePos)
	{
		if (index >= _boxes.Count) return;
		var box = _boxes[index];

		// Map mouse position to tilt angle — center = no tilt, edges = max tilt
		var nx = (mousePos.X / Size.X - 0.5f) * 2f;  // -1 to 1
		var ny = (mousePos.Y / Size.Y - 0.5f) * 2f;  // -1 to 1

		var tiltX = Mathf.DegToRad(-ny * 12f);  // tilt up/down
		var tiltY = Mathf.DegToRad( nx * 12f);  // tilt left/right

		// Handled differently on flipped box
		if (_flippedBoxes.Contains(index))
			tiltY = -tiltY;

		// Target rotation for flip offset
		var baseY = _flippedBoxes.Contains(index) ? Mathf.DegToRad(180f) : 0f;
		
		// Smoothly interpolate current rotation toward target
		var currentRot = box.Rotation;
		box.Rotation = new Vector3(
			Mathf.Lerp(currentRot.X, tiltX, 0.15f),
			Mathf.Lerp(currentRot.Y, baseY + tiltY, 0.15f),
			currentRot.Z
		);
	}
	
	// Method for snapping the game into place more fluidly after a swipe
	private void ElasticSnapSelected()
	{
		var index = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (index >= _boxes.Count) return;
		var box = _boxes[index];

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
	
	// Wrapping logic: offset is how far the box is from teh center and depth prevents distortion with distnace
	private void LayoutBoxes()
	{
		if (_boxes.Count == 0) return;

		_visibleBoxIndices.Clear();

		for (int index = 0; index < _boxes.Count; index++)
		{
			
			var box = _boxes[index];

			var d = index - CarouselPos;
			if (d > _count * 0.5f) d -= _count;
			if (d < -_count * 0.5f) d += _count;

			var visible = Mathf.Abs(d) <= VisibleCarouselDistance;
			if (box.Visible != visible)
				box.Visible = visible;
			if (!visible)
				continue;

			_visibleBoxIndices.Add(index);

			var t = Mathf.Clamp(Mathf.Abs(d), 0f, 1.5f);
			var alpha = Mathf.Lerp(1.0f, 0.55f, t);

			box.Position = new Vector3(d * Spacing, 0f, -t * 1.2f);
			
			// ONLY set scale and rotation if not hovered
			if (index != _hoveredIdx && !_returningBoxes.Contains(index))
			{
				var scale = Mathf.Lerp(SelectedScale, UnselectedScale, t);
				box.Scale = new Vector3(scale, scale, scale);
				if (!_flippedBoxes.Contains(index) && !_animatingBoxes.Contains(index))
					box.Rotation = new Vector3(0, Mathf.DegToRad(d * -8f), 0);
			}

			ApplyMaterialAlpha(index, alpha);
		}

		_lastLaidOutCarouselPos = CarouselPos;
		_layoutDirty = false;
	}

	private void ApplyPassiveSway(double delta, bool carouselMoving)
	{
		_swayTime += delta;
		var swayAngle = Mathf.Sin((float)_swayTime * SwaySpeed) * SwayAmplitude;
		var swayBob = Mathf.Sin((float)_swayTime * SwaySpeed * 2.0f) * SwayBobAmplitude;

		_currentSwayAngle = swayAngle;
		_bobStrength = carouselMoving
			? Mathf.Lerp(_bobStrength, 0f, 8f * (float)delta)
			: Mathf.Lerp(_bobStrength, 1f, 4f * (float)delta);

		foreach (var index in _visibleBoxIndices)
		{
			if (index == _hoveredIdx || _animatingBoxes.Contains(index) || _returningBoxes.Contains(index))
				continue;

			var box = _boxes[index];

			box.Rotation = new Vector3(
				swayAngle,
				box.Rotation.Y,
				swayAngle * 0.5f
			);

			box.Position = new Vector3(
				box.Position.X,
				swayBob * _bobStrength,
				box.Position.Z
			);
		}
	}

	private void ApplyMaterialAlpha(int index, float alpha)
	{
		if (index < _lastAppliedAlphas.Count &&
			!float.IsNaN(_lastAppliedAlphas[index]) &&
			Mathf.Abs(_lastAppliedAlphas[index] - alpha) < AlphaUpdateEpsilon)
		{
			return;
		}

		if (index < _baseMaterials.Count)
		{
			foreach (var mat in _baseMaterials[index])
				SetMaterialAlpha(mat, alpha);
		}
		if (index < _coverMaterials.Count && _coverMaterials[index] != null)
		{
			var coverMat = _coverMaterials[index];
			if (index >= _baseMaterials.Count || !_baseMaterials[index].Contains(coverMat))
				SetMaterialAlpha(coverMat, alpha);
		}

		if (index < _lastAppliedAlphas.Count)
			_lastAppliedAlphas[index] = alpha;
	}

	private static void SetMaterialAlpha(StandardMaterial3D mat, float alpha)
	{
		mat.AlbedoColor = new Color(
			mat.AlbedoColor.R,
			mat.AlbedoColor.G,
			mat.AlbedoColor.B,
			alpha);
	}

	private void MarkLayoutDirty()
	{
		_layoutDirty = true;
	}

	private void InvalidateMaterialAlpha(int index)
	{
		if (index >= 0 && index < _lastAppliedAlphas.Count)
			_lastAppliedAlphas[index] = float.NaN;
		MarkLayoutDirty();
	}

	private float WrapPos(float p)
	{
		if (_count == 0) return 0f;
		p %= _count;
		if (p < 0) p += _count;
		return p;
	}

	private int WrapIndex(int index)
	{
		if (_count == 0) return 0;
		index %= _count;
		if (index < 0) index += _count;
		return index;
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
		
		var now = Time.GetTicksMsec();
		if (now - _lastSpinTickMs < SpinAudioCooldownMs)
			return;
		
		_lastSpinTickMs = now;
		_lastSpinAudioStep = currentStep;
		PlaySpinAudio();
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
		CollapseExpandedGbaBacks();
		var current = Mathf.RoundToInt(CarouselPos);
		var target = WrapPos(current + dir);
		_velocity = 0f;
		CarouselPos = WrapPos(current);
		MarkLayoutDirty();
		
		_velocity = dir * 3.5f;
	}
	
	// FLIPPING THE GAME
	public void FlipSelected()
	{
		var index = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (index >= _boxes.Count) return;

		AudioManager.Instance?.PlayFlip();
		var box = _boxes[index];
		
		var isFlipped = _flippedBoxes.Contains(index);
		if (isFlipped)
		{
			_flippedBoxes.Remove(index);
			CollapseExpandedGbaBack(index);
		}
		else
			_flippedBoxes.Add(index);

		var targetY = isFlipped ? 0f : Mathf.DegToRad(180f);
		
		var currentRot = box.Rotation;
		
		_animatingBoxes.Add(index);
		_hoverTween?.Kill();
		_hoverTween = CreateTween();
		_hoverTween.SetTrans(Tween.TransitionType.Cubic);
		_hoverTween.SetEase(Tween.EaseType.InOut);
		_hoverTween.TweenProperty(box, "rotation",
			new Vector3(currentRot.X, targetY, currentRot.Z), 0.5f);
		var capturedIdx = index;
		_hoverTween.TweenCallback(Callable.From(() =>
			_animatingBoxes.Remove(capturedIdx)));
	}

	private void UnflipSelected()
	{
		CollapseExpandedGbaBacks();
		foreach (var index in _flippedBoxes)
		{
			if (index >= _boxes.Count) continue;
			
			_animatingBoxes.Add(index);
			var currentRot = _boxes[index].Rotation;
			
			var tween = CreateTween();
			tween.SetTrans(Tween.TransitionType.Cubic);
			tween.SetEase(Tween.EaseType.InOut);
			tween.TweenProperty(_boxes[index], "rotation",
				new Vector3(currentRot.X, 0f, currentRot.Z), 0.4f);
			
			var capturedIdx = index;
			tween.TweenCallback(Callable.From(() => 
				_animatingBoxes.Remove(capturedIdx)));
		}
		_flippedBoxes.Clear();
	}

	private bool TryToggleSelectedGbaBackDetail(Vector2 mousePosition)
	{
		if (!_isGba || _boxes.Count == 0 || IsCarouselMoving())
			return false;

		if (mousePosition.X < Size.X * 0.2f || mousePosition.X > Size.X * 0.8f)
			return false;

		var index = WrapIndex(Mathf.RoundToInt(CarouselPos));
		if (!_flippedBoxes.Contains(index))
			return false;

		ToggleGbaBackDetail(index, !_expandedGbaBacks.Contains(index));
		return true;
	}

	private void ToggleGbaBackDetail(int index, bool expanded)
	{
		if (!_isGba || index < 0 || index >= _boxes.Count)
			return;

		var box = _boxes[index];
		var compactBackMesh = FindMeshByName(box, "BackMesh");
		var detailBackMesh = FindMeshByName(box, "DetailBackMesh");
		if (compactBackMesh == null || detailBackMesh == null)
			return;

		compactBackMesh.Visible = !expanded;
		detailBackMesh.Visible = expanded;

		if (expanded)
			_expandedGbaBacks.Add(index);
		else
			_expandedGbaBacks.Remove(index);
	}

	private void CollapseExpandedGbaBack(int index)
	{
		if (!_expandedGbaBacks.Contains(index))
			return;

		ToggleGbaBackDetail(index, false);
	}

	private void CollapseExpandedGbaBacks()
	{
		if (_expandedGbaBacks.Count == 0)
			return;

		var expandedIndices = new List<int>(_expandedGbaBacks);
		foreach (var index in expandedIndices)
			ToggleGbaBackDetail(index, false);
	}

	private StandardMaterial3D ApplyDsLabelTexture(MeshInstance3D coverMesh, Texture2D? coverTexture,
		StandardMaterial3D? template)
	{
		var coverMat = template != null
			? (StandardMaterial3D)template.Duplicate()
			: new StandardMaterial3D();

		coverMat.AlbedoTexture = coverTexture;
		coverMat.AlbedoColor = coverTexture != null
			? Colors.White
			: new Color(0.86f, 0.88f, 0.94f, 1f);
		coverMat.Roughness = template?.Roughness ?? 0.55f;
		coverMat.Metallic = template?.Metallic ?? 0.04f;
		coverMat.Transparency = template?.Transparency ?? BaseMaterial3D.TransparencyEnum.Disabled;
		coverMat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		coverMat.ResourceLocalToScene = true;
		coverMat.Uv1Scale = new Vector3(DsCartridgeUvScaleU, DsCartridgeUvScaleV, 1f);
		coverMat.Uv1Offset = new Vector3(DsCartridgeUvOffsetU, DsCartridgeUvOffsetV, 0f);
		coverMesh.SetSurfaceOverrideMaterial(0, coverMat);
		return coverMat;
	}

	private static StandardMaterial3D ApplyCoverTexture(MeshInstance3D coverMesh, Texture2D texture,
		StandardMaterial3D? template)
	{
		var coverMat = template != null
			? (StandardMaterial3D)template.Duplicate()
			: new StandardMaterial3D();

		coverMat.AlbedoTexture = texture;
		coverMat.AlbedoColor = new Color(1, 1, 1, 1);
		coverMat.Roughness = template?.Roughness ?? 0.5f;
		coverMat.Metallic = template?.Metallic ?? 0.1f;
		// Preserve the source mat's render mode so opaque imported meshes stay in
		// Godot's opaque pass instead of breaking from alpha depth sorting.
		coverMat.Transparency = template?.Transparency ?? BaseMaterial3D.TransparencyEnum.Disabled;
		coverMat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		coverMat.ResourceLocalToScene = true;
		coverMesh.SetSurfaceOverrideMaterial(0, coverMat);
		return coverMat;
	}

	private static bool SquarePlatform(string? platformId)
	{
		return string.Equals(platformId, "ps1", System.StringComparison.OrdinalIgnoreCase) ||
			string.Equals(platformId, "gba", System.StringComparison.OrdinalIgnoreCase) ||
			string.Equals(platformId, "ds", System.StringComparison.OrdinalIgnoreCase);
	}

	private static bool N64Platform(string? platformId)
	{
		return string.Equals(platformId, "N64", System.StringComparison.OrdinalIgnoreCase) ||
			string.Equals(platformId, "n64", System.StringComparison.OrdinalIgnoreCase);
	}

	private static bool SidewaysPlatform(string? platformId)
	{
		return string.Equals(platformId, "snes", System.StringComparison.OrdinalIgnoreCase);
	}

	private static PackedScene? LoadPackedScene(string path, ref PackedScene? cachedScene)
	{
		if (cachedScene != null)
			return cachedScene;

		if (!ResourceLoader.Exists(path))
			return null;

		cachedScene = GD.Load<PackedScene>(path);
		return cachedScene;
	}

	private static bool SquareBox(Node node)
	{
		return BoxMeta(node, "pgemu_square_box");
	}

	private static bool LandscapeBox(Node node)
	{
		return BoxMeta(node, "pgemu_landscape_box");
	}

	private static bool BoxMeta(Node node, string key)
	{
		for (Node? current = node; current != null; current = current.GetParent())
		{
			if (current.HasMeta(key))
				return current.GetMeta(key).AsBool();
		}

		return false;
	}

	private Texture2D? GetGbaCartridgeLogoTexture()
	{
		if (_gbaCartridgeLogoTexture != null)
			return _gbaCartridgeLogoTexture;

		var absolutePath = ProjectSettings.GlobalizePath(GbaCartridgeLogoPath);
		if (!FileAccess.FileExists(absolutePath))
		{
			GD.PrintErr($"Missing GBA cartridge logo at {absolutePath}");
			return null;
		}

		var image = Image.LoadFromFile(absolutePath);
		if (image.GetWidth() == 0 || image.GetHeight() == 0)
		{
			GD.PrintErr($"Unable to load GBA cartridge logo from {absolutePath}");
			return null;
		}

		_gbaCartridgeLogoTexture = ImageTexture.CreateFromImage(image);
		return _gbaCartridgeLogoTexture;
	}

	private static StandardMaterial3D ApplyGbaPlaceholderTexture(MeshInstance3D coverMesh, Texture2D texture,
		StandardMaterial3D? template)
	{
		var coverMat = ApplyCoverTexture(coverMesh, texture, template);
		coverMat.Uv1Scale = new Vector3(GbaCartridgeUvScaleU, GbaCartridgeUvScaleV, 1f);
		coverMat.Uv1Offset = new Vector3(GbaCartridgeUvOffsetU, GbaCartridgeUvOffsetV, 0f);
		coverMesh.SetSurfaceOverrideMaterial(0, coverMat);
		return coverMat;
	}

	private static MeshInstance3D? FindMeshByName(Node node, string name)
	{
		return FindMesh(node, mesh => mesh.Name == name);
	}

	private static MeshInstance3D? FindFirstMeshInstance(Node node)
	{
		return FindMesh(node, mesh => mesh.Mesh != null);
	}

	private static MeshInstance3D? FindMeshInstanceByDescriptor(Node node, string needle)
	{
		var lowerNeedle = needle.ToLowerInvariant();
		return FindMesh(node, mesh => MeshDescriptorContains(mesh, lowerNeedle));
	}

	private static MeshInstance3D? FindMesh(Node node, System.Func<MeshInstance3D, bool> match)
	{
		if (node is MeshInstance3D mesh && match(mesh))
			return mesh;

		foreach (Node child in node.GetChildren())
		{
			var found = FindMesh(child, match);
			if (found != null)
				return found;
		}

		return null;
	}

	private static bool MeshDescriptorContains(MeshInstance3D mesh, string needle)
	{
		if (mesh.Mesh == null)
			return false;

		var meshDescriptor = $"{mesh.Name} {mesh.Mesh.ResourceName} {mesh.Mesh.ResourcePath}".ToLowerInvariant();
		if (meshDescriptor.Contains(needle, System.StringComparison.Ordinal))
			return true;

		for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
		{
			if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D mat)
				continue;

			var materialDescriptor = $"{mat.ResourceName} {mat.ResourcePath}".ToLowerInvariant();
			if (materialDescriptor.Contains(needle, System.StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	private static void CollectImportedMaterials(Node node, List<StandardMaterial3D> materials,
		ref MeshInstance3D? labelMesh)
	{
		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				if (mesh.Mesh.SurfaceGetMaterial(surface) is not StandardMaterial3D baseMat)
					continue;

				var duplicate = (StandardMaterial3D)baseMat.Duplicate();
				duplicate.ResourceLocalToScene = true;
				duplicate.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
				mesh.SetSurfaceOverrideMaterial(surface, duplicate);
				materials.Add(duplicate);

				if (labelMesh == null && MaterialLooksLikeGbaLabel(baseMat, mesh))
					labelMesh = mesh;
			}
		}

		foreach (Node child in node.GetChildren())
			CollectImportedMaterials(child, materials, ref labelMesh);
	}

	private static bool MaterialLooksLikeGbaLabel(StandardMaterial3D mat, MeshInstance3D mesh)
	{
		var descriptor = $"{mat.ResourceName} {mat.ResourcePath} {mesh.Name}".ToLowerInvariant();
		return descriptor.Contains("label") || descriptor.Contains("sticker");
	}

	private static void CenterNode3D(Node3D root)
	{
		if (!TryGetNodeBounds(root, Transform3D.Identity, out var bounds))
			return;

		root.Position -= bounds.GetCenter();
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
}
