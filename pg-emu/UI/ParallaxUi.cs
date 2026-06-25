using Godot;

namespace PGEmu.UI;

public partial class ParallaxUi : Node
{
    [Export] public CanvasLayer[] MidLayers = System.Array.Empty<CanvasLayer>();
    [Export] public CanvasLayer[] ForegroundLayers = System.Array.Empty<CanvasLayer>();
       
    private Vector2 _current = Vector2.Zero;
    private const float Strength = 2f;
    private const float Smoothing = 5f;
    
    public override void _Process(double delta)
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var mouse = GetViewport().GetMousePosition();

        var normalized = new Vector2(
            (mouse.X / viewport.X - 0.5f) * 2f,
            (mouse.Y / viewport.Y - 0.5f) * 2f
        );

        _current = _current.Lerp(normalized * Strength, Smoothing * (float)delta);

        foreach (var layer in MidLayers)
            layer.Offset = _current * 0.4f;

        foreach (var layer in ForegroundLayers)
            layer.Offset = _current * 0.8f;
    }
}