using Godot;
using System.Collections.Generic;

namespace PGEmu.Services;

public partial class CollectionCarousel3DView : SubViewportContainer
{
	private const ulong SettleSpinSuppressWindowMs = 40;
	private const string FolderModelPath = "res://Models/icon_folder.glb";
	private static readonly Color FolderBodyColor = new(0.535f, 0.446f, 0.93f, 1f);
	private static readonly Color FolderTabColor = new(0.80f, 0.62f, 1.0f, 1f);
	private static readonly Color FolderPaperColor = new(0.95f, 0.93f, 1.0f, 1f);
	private static readonly Color FolderDarkColor = new(0.18f, 0.14f, 0.32f, 1f);

	private static readonly Vector3 CameraDefaultPosition = new(0f, 0.48f, 4.75f);
	private static readonly Vector3 CameraDefaultRotation = new(Mathf.DegToRad(-5f), 0f, 0f);
	private const float CameraDefaultFov = 57f;

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

	// Carousel state
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
	private SubViewport _viewport = null!;
	private Node3D _sceneRoot = null!;
	private Camera3D _camera = null!;
	private PackedScene? _folderScene;
	private readonly List<Node3D> _boxes = new();

	// Card spacing in 3D units
	private const float Spacing = 2.30f;
	private const float SelectedScale = 1.0f;
	private const float UnselectedScale = 0.62f;
	private const float SelectedDepth = 0.35f;
	private const float UnselectedDepth = -0.08f;
	private const float FolderBaselineY = -0.18f;

	// Tweening animation
	private int _hoveredIdx = -1;
	private Tween? _hoverTween;
	private double _mouseIdleTime = 0f;
	private const double MouseIdleThreshold = 1.0;

	// Folder colors
	private readonly Dictionary<StandardMaterial3D, Color> _originalColors = new();

	public event System.Action<int>? SelectionChanged;

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
			Position = CameraDefaultPosition,
			Rotation = CameraDefaultRotation,
		};
		_camera.Fov = CameraDefaultFov;
		_sceneRoot.AddChild(_camera);

		// Lighting
		var sun = new DirectionalLight3D
		{
			LightEnergy = 2.55f,
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
			LightEnergy = 1.25f,
			LightColor = new Color(0.45f, 0.38f, 1f),
		};
		fill.RotateX(Mathf.DegToRad(20f));
		fill.RotateY(Mathf.DegToRad(-120f));
		_sceneRoot.AddChild(fill);

		var frontFill = new OmniLight3D
		{
			Position = new Vector3(0f, 0.55f, 3.05f),
			LightEnergy = 1.35f,
			OmniRange = 9f,
			LightColor = new Color(0.85f, 0.82f, 1.0f),
		};
		_sceneRoot.AddChild(frontFill);

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
		env.AmbientLightColor = new Color(0.38f, 0.38f, 0.78f);
		env.AmbientLightEnergy = 1.15f;

		var worldEnv = new WorldEnvironment { Environment = env };
		_sceneRoot.AddChild(worldEnv);

		// Ground for receiving shadows
		var ground = new MeshInstance3D();
		ground.Mesh = new PlaneMesh { Size = new Vector2(80f, 24f) };
		ground.Position = new Vector3(0f, -1.12f, 0.55f);
		ground.RotateX(Mathf.DegToRad(-3f));

		var groundMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.08f, 0.05f, 0.25f, 0.32f),
			Roughness = 1f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha
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
	}

	// Call this from Collections after loading collection names to clear everything and rebuild.
	public void Populate(IReadOnlyList<string> collections, float initialPos = 0f)
	{
		ClearFolders();

		_count = collections.Count;
		CarouselPos = WrapPos(initialPos);
		_spinAudioPos = CarouselPos;
		_lastSpinAudioCarouselPos = CarouselPos;
		_lastSpinAudioStep = Mathf.RoundToInt(_spinAudioPos);
		_lastSpinAudioMs = 0;

		for (int i = 0; i < collections.Count; i++)
		{
			var box = BuildFolder(collections[i]);
			_sceneRoot.AddChild(box);
			_boxes.Add(box);
		}

		LayoutBoxes();
	}

	public void SetCarouselPos(float position)
	{
		_velocity = 0f;
		CarouselPos = WrapPos(position);
		LayoutBoxes();
		SelectionChanged?.Invoke(WrapIndex(Mathf.RoundToInt(CarouselPos)));
	}

	private void ClearFolders()
	{
		foreach (var box in _boxes)
			box.QueueFree();

		_boxes.Clear();
		_originalColors.Clear();
		_hoveredIdx = -1;
		_hoverTween?.Kill();
		_hoverTween = null;
	}

	// Builds actual 3d model of the collection folder.
	private Node3D BuildFolder(string collectionName)
	{
		if (!ResourceLoader.Exists(FolderModelPath))
		{
			GD.PrintErr($"Folder model not found at {FolderModelPath}, using placeholder");
			return BuildPlaceholder(collectionName);
		}

		_folderScene ??= GD.Load<PackedScene>(FolderModelPath);
		var model = _folderScene.Instantiate<Node3D>();
		EnableShadows(model);
		ApplyFolderPalette(model);

		var wrapper = new Node3D { Name = string.IsNullOrWhiteSpace(collectionName) ? "Collection" : collectionName };
		var modelRoot = SetupModel(model);
		wrapper.AddChild(modelRoot);
		return wrapper;
	}

	private static Node3D SetupModel(Node3D model)
	{
		CenterNode3D(model);
		var scale = ScaleToFit(model, 2.52f);
		model.Scale = new Vector3(scale, scale, scale);
		model.Position = new Vector3(0f, -0.40f, 0f);
		model.RotateX(Mathf.DegToRad(-5f));
		model.RotateY(Mathf.DegToRad(-18f));
		return model;
	}

	// Fallback for if the folder model is missing.
	private Node3D BuildPlaceholder(string collectionName)
	{
		var root = new Node3D { Name = string.IsNullOrWhiteSpace(collectionName) ? "Collection" : collectionName };
		var body = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(2.1f, 1.25f, 0.28f) },
			Position = new Vector3(0f, -0.08f, 0f)
		};
		body.SetSurfaceOverrideMaterial(0, MakeMat(FolderBodyColor));
		root.AddChild(body);

		var tab = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.82f, 0.36f, 0.26f) },
			Position = new Vector3(-0.55f, 0.62f, -0.01f)
		};
		tab.SetSurfaceOverrideMaterial(0, MakeMat(FolderTabColor));
		root.AddChild(tab);
		root.Position = new Vector3(0f, -0.40f, 0f);
		root.RotateX(Mathf.DegToRad(-5f));
		root.RotateY(Mathf.DegToRad(-18f));
		return root;
	}

	private static StandardMaterial3D MakeMat(Color color, float roughness = 0.5f, float metallic = 0.2f) =>
		new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, };

	private void ApplyFolderPalette(Node node)
	{
		if (node is MeshInstance3D mesh && mesh.Mesh != null)
		{
			var surfaceCount = mesh.Mesh.GetSurfaceCount();
			for (int surface = 0; surface < surfaceCount; surface++)
			{
				var material = GetOrCreateOverrideMaterial(mesh, surface);
				if (material == null)
					continue;

				var sourceColor = material.AlbedoColor;
				var targetColor = ChooseFolderColor(sourceColor, material.ResourceName);
				material.AlbedoColor = targetColor;
				material.Roughness = Mathf.Max(material.Roughness, 0.45f);
				material.Metallic = Mathf.Min(material.Metallic, 0.05f);
				material.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
				_originalColors[material] = targetColor;
			}
		}

		foreach (Node child in node.GetChildren())
			ApplyFolderPalette(child);
	}

	private static StandardMaterial3D? GetOrCreateOverrideMaterial(MeshInstance3D mesh, int surface)
	{
		if (mesh.GetSurfaceOverrideMaterial(surface) is StandardMaterial3D overrideMaterial)
			return overrideMaterial;

		StandardMaterial3D material;
		if (mesh.Mesh?.SurfaceGetMaterial(surface) is StandardMaterial3D sourceMaterial)
		{
			material = (StandardMaterial3D)sourceMaterial.Duplicate();
		}
		else
		{
			material = new StandardMaterial3D
			{
				AlbedoColor = Colors.White,
				Roughness = 0.55f,
				Metallic = 0f,
			};
		}

		mesh.SetSurfaceOverrideMaterial(surface, material);
		return material;
	}

	private static Color ChooseFolderColor(Color sourceColor, string materialName)
	{
		var name = materialName ?? string.Empty;
		var brightness = (sourceColor.R + sourceColor.G + sourceColor.B) / 3f;
		var maxChannel = Mathf.Max(sourceColor.R, Mathf.Max(sourceColor.G, sourceColor.B));

		if (name.Contains("paper", System.StringComparison.OrdinalIgnoreCase) ||
			name.Contains("page", System.StringComparison.OrdinalIgnoreCase) ||
			maxChannel > 0.88f)
		{
			return FolderPaperColor;
		}

		if (brightness < 0.18f)
			return Mix(FolderDarkColor, FolderBodyColor, 0.18f);

		if (sourceColor.R > sourceColor.B + 0.08f && sourceColor.G > sourceColor.B + 0.03f)
			return Mix(FolderBodyColor, FolderTabColor, Mathf.Clamp(brightness, 0.25f, 0.75f));

		return Mix(FolderDarkColor, FolderBodyColor, Mathf.Clamp(0.62f + brightness * 0.42f, 0.62f, 1f));
	}

	private static Color Mix(Color a, Color b, float t)
	{
		t = Mathf.Clamp(t, 0f, 1f);
		return new Color(
			Mathf.Lerp(a.R, b.R, t),
			Mathf.Lerp(a.G, b.G, t),
			Mathf.Lerp(a.B, b.B, t),
			Mathf.Lerp(a.A, b.A, t));
	}

	private static void CenterNode3D(Node3D root)
	{
		if (!TryGetNodeBounds(root, Transform3D.Identity, out var bounds))
			return;

		root.Position -= bounds.GetCenter();
	}

	private static float ScaleToFit(Node3D root, float targetMaxDimension)
	{
		if (targetMaxDimension <= 0f || !TryGetNodeBounds(root, Transform3D.Identity, out var bounds))
			return 1f;

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
			// anistropic filtering
			for (int s = 0; s < mesh.GetSurfaceOverrideMaterialCount(); s++)
			{
				var mat = mesh.GetSurfaceOverrideMaterial(s) as StandardMaterial3D;
				if (mat != null)
				{
					mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
					mat.Roughness = Mathf.Max(mat.Roughness, 0.25f);
					mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
				}
			}
		}

		foreach (var child in node.GetChildren())
			if (child is Node3D childNode)
				EnableShadows(childNode);
	}

	// Physics processing for the spin
	public override void _Process(double delta)
	{
		_mouseIdleTime += delta;
		_spinSpeed = Mathf.Lerp(_spinSpeed, 0f, SpinSpeedDecay * (float)delta);

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

		// Idle spin on selected folder
		var selectedIdx = WrapIndex(Mathf.RoundToInt(CarouselPos));
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

	// Handle when the mouse is clicked or dragged, kills velocity so it doesn't drift when you drag
	public override void _GuiInput(InputEvent e)
	{
		if (InputRoutingService.Instance?.IsUiInputBlocked == true)
		{
			AcceptEvent();
			return;
		}
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
				if (wasClick)
				{
					_velocity = 0f;
					CarouselPos = WrapPos(Mathf.Round(CarouselPos));
					SelectionChanged?.Invoke(WrapIndex(Mathf.RoundToInt(CarouselPos)));
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
		if (InputRoutingService.Instance?.IsUiInputBlocked == true)
			return;
		if (_boxes.Count == 0) return;
		if (e is InputEventMouseMotion)
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
		var tiltY = Mathf.DegToRad(nx * 12f);  // tilt left/right

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

	// Wrapping logic: d is how far the box is from the center and t prevents distortion with distance
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
			var depthT = t / 1.5f;

			box.Position = new Vector3(
				d * Spacing,
				FolderBaselineY + depthT * 0.18f,
				Mathf.Lerp(SelectedDepth, UnselectedDepth, depthT));

			// ONLY set scale and rotation if not hovered
			if (i != _hoveredIdx)
			{
				var scale = Mathf.Lerp(SelectedScale, UnselectedScale, depthT);
				box.Scale = new Vector3(scale, scale, scale);
				box.Rotation = new Vector3(0, Mathf.DegToRad(d * -7f), 0);
			}

			var brightness = Mathf.Lerp(1.18f, 0.72f, depthT);
			var lift = Mathf.Lerp(0.055f, 0.025f, depthT);
			DimMeshes(box, brightness, lift);
		}
	}

	// Dims the models that are in the background
	private void DimMeshes(Node node, float brightness, float lift)
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
					Mathf.Clamp(original.R * brightness + lift, 0f, 1f),
					Mathf.Clamp(original.G * brightness + lift, 0f, 1f),
					Mathf.Clamp(original.B * brightness + lift, 0f, 1f),
					original.A);
			}
		}
		foreach (Node child in node.GetChildren())
			DimMeshes(child, brightness, lift);
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
		_spinSpeed = Mathf.Clamp(speedSource / 4.0f, 0f, SpinSpeedMax);

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
