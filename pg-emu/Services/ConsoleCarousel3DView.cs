using Godot;
using System.Collections.Generic;

namespace PGEmu.Services;

public partial class ConsoleCarousel3DView : SubViewportContainer
{
    private const ulong SettleSpinSuppressWindowMs = 90;
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
        ground.Mesh = new PlaneMesh { Size = new Vector2(200f, 50f) };
        // position below the consoles
        ground.Position = new Vector3(0, -1.2f, 2f); 
        ground.RotateX(Mathf.DegToRad(-4f));
        
        var groundMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.05f, 0.15f, 0.5f),
            Roughness = 1f, 
            // ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // ShadowToOpacity = true,
        };
        ground.SetSurfaceOverrideMaterial(0, groundMat);
        ground.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _sceneRoot.AddChild(ground);
        
        // Shadows
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        // Aliasing
        _viewport.UseTaa = false;
        _viewport.Msaa3D = Viewport.Msaa.Msaa8X;
        //_viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        
    }

    // Call this from GameSelect after loading games to clear everything and rebuild
    public void Populate(List<ConsoleType> consoles, float initialPos = 0f)
    {
        // Clear old boxes
        foreach (var b in _boxes)
            b.QueueFree();
        _boxes.Clear();

        
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
            // ConsoleType.GBA          => "res://Models/gba.glb",
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
        EnableShadows(model);
        var wrapper = new Node3D { Name = type.ToString() };
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
                model.Scale = new Vector3(1.1f, 1.1f, 1.1f);
                model.Position = new Vector3(0.3f, -1f, 0);
                model.RotateX(Mathf.DegToRad(-10));
                model.RotateY(Mathf.DegToRad(30));
                break;
            case ConsoleType.GameCube:
                model.Scale = new Vector3(0.030f, 0.030f, 0.030f);
                model.Position = new Vector3(0, -0.7f, 0);
                model.RotateY(Mathf.DegToRad(30f));
                break;
            case ConsoleType.GBA:
                model.Scale = new Vector3(0.8f, 0.8f, 0.8f);
                model.RotateY(Mathf.DegToRad(30f));
                break;
        }

        wrapper.AddChild(model);
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
        new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
    
    
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
        GD.Print($"GuiInput: {e.GetType().Name}");
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
                _lastDragX = mb.Position.X;
                _lastDragVelocity = 0f;
                _velocity = 0f;
                GetViewport().SetInputAsHandled();
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
