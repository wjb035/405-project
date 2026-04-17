namespace PGEmu.UI;

using Godot;
using System.Threading.Tasks;

public partial class ToastNotification : Control
{
    private Label _label;
    private PanelContainer _panel;
    private Tween _tween;
    
    public override void _Ready()
    {
        _panel = new PanelContainer();
        _label = new Label();
        
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.AddThemeFontSizeOverride("font_size", 20);
        
        margin.AddChild(_label);
        _panel.AddChild(margin);
        AddChild(_panel);
        
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.1f, 0.08f, 0.2f, 0.95f);
        styleBox.BorderColor = new Color(0.6f, 0.5f, 0.9f, 0.8f);
        styleBox.BorderWidthLeft = 2;
        styleBox.BorderWidthTop = 2;
        styleBox.BorderWidthRight = 2;
        styleBox.BorderWidthBottom = 2;
        styleBox.CornerRadiusTopLeft = 16;
        styleBox.CornerRadiusTopRight = 16;
        styleBox.CornerRadiusBottomRight = 16;
        styleBox.CornerRadiusBottomLeft = 16;
        _panel.AddThemeStyleboxOverride("panel", styleBox);
        
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 1f;
        AnchorTop = 1f;
        OffsetLeft = -250f;
        OffsetRight = 250f;
        OffsetBottom = -80f;
        OffsetTop = -140f;
        
        Modulate = new Color(1, 1, 1, 0);
    }
    
    public async Task Show(string message, float duration = 2.0f)
    {
        _label.Text = message;
        
        _tween?.Kill();
        _tween = CreateTween();
        
        _tween.TweenProperty(this, "modulate:a", 1.0f, 0.2f);
        await ToSignal(_tween, "finished");
        
        await ToSignal(GetTree().CreateTimer(duration), "timeout");
        
        _tween = CreateTween();
        _tween.TweenProperty(this, "modulate:a", 0.0f, 0.3f);
        await ToSignal(_tween, "finished");
        
        QueueFree();
    }
}