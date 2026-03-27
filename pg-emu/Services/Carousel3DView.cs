using Godot;
using System.Collections.Generic;

namespace PGEmu.Services;

public partial class Carousel3DView : SubViewportContainer
{
    private const ulong SettleSpinSuppressWindowMs = 90;
    // Physics
    private const float HoverMotionThreshold = 0.001f;
    private float _velocity = 0f;
    private const float Friction = 4.5f;
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

    // 3D scene internals
    private SubViewport _viewport;
    private Node3D _sceneRoot;
    private Camera3D _camera;
    private readonly List<Node3D> _boxes = new();
    private readonly List<MeshInstance3D> _meshes = new();
    private readonly List<StandardMaterial3D> _baseMaterials = new();
    
    // Card spacing in 3D units
    private const float Spacing = 2.2f;
    private const float SelectedScale = 1.0f;
    private const float UnselectedScale = 0.72f;

    // Tweening animation
    private int _hoveredIdx = -1;
    private Tween? _hoverTween;
    private double _mouseIdleTime = 0f;
    private const double MouseIdleThreshold = 1.0; 
    
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
            LightEnergy = 1.1f,
            LightColor = new Color(0.85f, 0.80f, 1.0f),
        };
        sun.RotateX(Mathf.DegToRad(-45f));
        _sceneRoot.AddChild(sun);

        var ambient = new OmniLight3D
        {
            Position = new Vector3(0, 2, 4),
            LightEnergy = 0.5f,
            OmniRange = 20f,
            LightColor = new Color(0.62f, 0.52f, 0.90f),
        };
        _sceneRoot.AddChild(ambient);
        
        var rim = new OmniLight3D
        {
            Position = new Vector3(0, 1f, -3f),
            LightEnergy = 0.4f,
            OmniRange = 12f,
            LightColor = new Color(0.70f, 0.88f, 1.0f), 
        };
        _sceneRoot.AddChild(rim);
        
        // Aliasing
        _viewport.Msaa3D = Viewport.Msaa.Msaa4X;
        _viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        
    }

    // Call this from GameSelect after loading games to clear everything and rebuild
    public void Populate(List<(string title, Texture2D? coverArt)> games, float initialPos)
    {
        // Clear old boxes
        foreach (var b in _boxes)
            b.QueueFree();
        _boxes.Clear();
        _meshes.Clear();
        _baseMaterials.Clear();
        
        _count = games.Count;
        CarouselPos = WrapPos(initialPos);
        _spinAudioPos = CarouselPos;
        _lastSpinAudioCarouselPos = CarouselPos;
        _lastSpinAudioStep = Mathf.RoundToInt(_spinAudioPos);
        _lastSpinAudioMs = 0;

        for (int i = 0; i < games.Count; i++)
        {
            var (title, coverArt) = games[i];
            var box = BuildBox(title, coverArt);
            _sceneRoot.AddChild(box);
            _boxes.Add(box);
            _meshes.Add(box.GetNode<MeshInstance3D>("Mesh"));
        }

        LayoutBoxes();
    }
    
    // Builds actual 3d geometry of the cases
    private Node3D BuildBox(string title, Texture2D? coverArt)
    {
        var root = new Node3D();

        // Box mesh 
        var mesh = new MeshInstance3D { Name = "Mesh" };
        var boxMesh = new BoxMesh
        {
            Size = new Vector3(2.6f, 3.6f, 0.25f)
        };
        mesh.Mesh = boxMesh;

        // Material — dark base + cover art on the front
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.06f, 0.14f),
            RoughnessTexture = null,
            Roughness = 0.6f,
            Metallic = 0.2f,
        };
        
        _baseMaterials.Add(mat);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mesh.SetSurfaceOverrideMaterial(0, mat);
        root.AddChild(mesh);
        
        // Cover art as a separate quad on the front face
        var coverMesh = new MeshInstance3D { Name = "CoverMesh" };
        var quad = new QuadMesh
        {
            Size = new Vector2(2.5f, 3.5f)
        };
        coverMesh.Mesh = quad;
        coverMesh.Position = new Vector3(0, 0, 0.26f);
        
        if (coverArt != null)
        {
            // Front face material with cover art
            var coverMat = new StandardMaterial3D
            {
                AlbedoTexture = coverArt,
                AlbedoColor = new Color(1, 1, 1, 1),
                Roughness = 0.5f,
                Metallic = 0.1f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            
            coverMesh.SetSurfaceOverrideMaterial(0, coverMat);
            
        }
        root.AddChild(coverMesh);
        
        // DEBUG
        if (coverArt != null)
            GD.Print($"Cover art found for: {title}");
        else
            GD.Print($"No cover art for: {title}");
        
        return root;
    }
    
    // Updates a box's texture,c all it from gameselect
    public void UpdateCoverArt(int index, Texture2D texture)
    {
        if (index >= _meshes.Count) return;
        var coverMesh = _boxes[index].GetNodeOrNull<MeshInstance3D>("CoverMesh");
        if (coverMesh == null) return;
    
        var coverMat = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = new Color(1, 1, 1, 1),
            Roughness = 0.5f,
            Metallic = 0.1f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        coverMesh.SetSurfaceOverrideMaterial(0, coverMat);
        
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
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (mb.Position.Y < 70f)
                    return;
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
            CarouselPos = WrapPos(_dragStartPos - dx * DragScale * 5f);
            UpdateSpinAudioFromMotion();

            // Track velocity for fling
            _lastDragVelocity = (mm.Position.X - _lastDragX) * -0.001f * FlingMultiplier * 30f;
            _lastDragX = mm.Position.X;

            SelectionChanged?.Invoke(WrapIndex(Mathf.RoundToInt(CarouselPos)));
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
            var alpha = Mathf.Lerp(1.0f, 0.4f, t);

            box.Position = new Vector3(d * Spacing, 0f, -t * 1.2f);
            
            // ONLY set scale and rotation if not hovered
            if (i != _hoveredIdx)
            {
                var scale = Mathf.Lerp(SelectedScale, UnselectedScale, t);
                box.Scale = new Vector3(scale, scale, scale);
                box.Rotation = new Vector3(0, Mathf.DegToRad(d * -8f), 0);
            }

            if (i < _baseMaterials.Count)
            {
                _baseMaterials[i].AlbedoColor = new Color(
                    _baseMaterials[i].AlbedoColor.R,
                    _baseMaterials[i].AlbedoColor.G,
                    _baseMaterials[i].AlbedoColor.B,
                    alpha);
            }
        }
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
        _velocity = dir * -8f; 
    }
    
}
