using Godot;

namespace PGEmu.Services;

public static class UiStyle
{
	// Text colors
	private static readonly Color TextColor = new(0.95f, 0.94f, 1f, 0.98f);
	private static readonly Color MutedTextColor = new(0.87f, 0.86f, 0.96f, 0.9f);

	// Main button styling.
	public static void StylePrimaryButton(Button? button)
	{
		if (button == null) return;
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.24f, 0.19f, 0.36f, 0.92f), new Color(0.86f, 0.68f, 1f, 1f), 2));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.30f, 0.24f, 0.44f, 0.97f), new Color(0.91f, 0.77f, 1f, 1f), 2));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(new Color(0.20f, 0.16f, 0.30f, 1f), new Color(0.82f, 0.62f, 1f, 1f), 2));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.28f, 0.21f, 0.40f, 0.98f), new Color(0.76f, 0.90f, 1f, 1f), 3));
		button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.20f, 0.20f, 0.23f, 0.55f), new Color(0.52f, 0.52f, 0.56f, 0.5f), 1));
		button.AddThemeColorOverride("font_color", TextColor);
		button.AddThemeColorOverride("font_hover_color", TextColor);
		button.AddThemeColorOverride("font_pressed_color", TextColor);
		button.AddThemeColorOverride("font_focus_color", TextColor);
		button.AddThemeColorOverride("font_disabled_color", new Color(0.82f, 0.82f, 0.88f, 0.6f));
	}

	public static void StyleNavButton(Button? button)
	{
		if (button == null) return;
		StylePrimaryButton(button);
		button.CustomMinimumSize = new Vector2(64f, 64f);
	}

	// Compact variant for top-row utility buttons.
	public static void StyleTopBarButton(Button? button)
	{
		if (button == null) return;
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.18f, 0.14f, 0.27f, 0.75f), new Color(0.64f, 0.54f, 0.82f, 0.7f), 2));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.24f, 0.19f, 0.35f, 0.9f), new Color(0.88f, 0.72f, 1f, 0.95f), 2));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(new Color(0.15f, 0.12f, 0.23f, 0.92f), new Color(0.79f, 0.62f, 1f, 1f), 2));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.22f, 0.17f, 0.31f, 0.92f), new Color(0.76f, 0.90f, 1f, 1f), 3));
		button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.20f, 0.20f, 0.23f, 0.55f), new Color(0.52f, 0.52f, 0.56f, 0.5f), 1));
		button.AddThemeColorOverride("font_color", TextColor);
		button.AddThemeColorOverride("font_hover_color", TextColor);
		button.AddThemeColorOverride("font_pressed_color", TextColor);
		button.AddThemeColorOverride("font_focus_color", TextColor);
	}
	
	// style popup menus like the dropdown in user profiles
		public static void StylePopupMenu(PopupMenu? button)
	{
		if (button == null) return;
		var stylebox = new StyleBoxFlat();
		stylebox.SetBgColor(new Color(0.18f, 0.14f, 0.27f, 0.75f));
		button.AddThemeStyleboxOverride("panel", stylebox);
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.18f, 0.14f, 0.27f, 0.75f), new Color(0.64f, 0.54f, 0.82f, 0.7f), 1));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.24f, 0.19f, 0.35f, 0.9f), new Color(0.88f, 0.72f, 1f, 0.95f), 2));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(new Color(0.15f, 0.12f, 0.23f, 0.92f), new Color(0.79f, 0.62f, 1f, 1f), 2));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.22f, 0.17f, 0.31f, 0.92f), new Color(0.76f, 0.90f, 1f, 1f), 2));
		button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.20f, 0.20f, 0.23f, 0.55f), new Color(0.52f, 0.52f, 0.56f, 0.5f), 1));
		button.AddThemeColorOverride("font_color", TextColor);
		button.AddThemeColorOverride("font_hover_color", TextColor);
		button.AddThemeColorOverride("font_pressed_color", TextColor);
		button.AddThemeColorOverride("font_focus_color", TextColor);
	}
	

	// Reduce internal text padding when layout spacing should come from containers.
	public static void TightenButtonContentPadding(Button? button, float horizontal = 4f, float vertical = 4f)
	{
		if (button == null) return;

		string[] states = { "normal", "hover", "pressed", "focus", "disabled" };
		foreach (string state in states)
		{
			if (button.GetThemeStylebox(state) is not StyleBoxFlat flat)
				continue;

			var tuned = (StyleBoxFlat)flat.Duplicate();
			tuned.ContentMarginLeft = horizontal;
			tuned.ContentMarginRight = horizontal;
			tuned.ContentMarginTop = vertical;
			tuned.ContentMarginBottom = vertical;
			button.AddThemeStyleboxOverride(state, tuned);
		}
	}

	public static void StyleTitleLabel(Label? label)
	{
		if (label == null) return;
		// A light shadow
		label.AddThemeColorOverride("font_color", TextColor);
		label.AddThemeColorOverride("font_shadow_color", new Color(0.05f, 0.04f, 0.08f, 0.9f));
	}

	// Secondary metadata text (counts, badges, small helper info).
	public static void StyleMetaLabel(Label? label)
	{
		if (label == null) return;
		label.AddThemeColorOverride("font_color", MutedTextColor);
	}

	// Status line color to differentiate feedback from titles.
	public static void StyleStatusLabel(Label? label)
	{
		if (label == null) return;
		label.AddThemeColorOverride("font_color", new Color(0.80f, 0.88f, 0.96f, 0.95f));
	}

	// Input field style for collection naming and future text prompts.
	public static void StyleLineEdit(LineEdit? lineEdit)
	{
		if (lineEdit == null) return;
		lineEdit.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.10f, 0.10f, 0.16f, 0.9f), new Color(0.66f, 0.60f, 0.85f, 0.8f), 1));
		lineEdit.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.12f, 0.11f, 0.18f, 0.95f), new Color(0.76f, 0.90f, 1f, 1f), 2));
		lineEdit.AddThemeColorOverride("font_color", TextColor);
		lineEdit.AddThemeColorOverride("font_placeholder_color", new Color(0.78f, 0.76f, 0.88f, 0.45f));
		lineEdit.AddThemeColorOverride("caret_color", new Color(0.89f, 0.84f, 1f, 1f));
	}

	public static void StyleTextEdit(TextEdit? textEdit)
	{
		if (textEdit == null) return;
		textEdit.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.10f, 0.10f, 0.16f, 0.9f), new Color(0.66f, 0.60f, 0.85f, 0.8f), 1));
		textEdit.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.12f, 0.11f, 0.18f, 0.95f), new Color(0.76f, 0.90f, 1f, 1f), 2));
		textEdit.AddThemeStyleboxOverride("read_only", CreateButtonStyle(new Color(0.08f, 0.08f, 0.12f, 0.7f), new Color(0.42f, 0.40f, 0.55f, 0.5f), 1));
		textEdit.AddThemeColorOverride("font_color", TextColor);
		textEdit.AddThemeColorOverride("font_placeholder_color", new Color(0.78f, 0.76f, 0.88f, 0.45f));
		textEdit.AddThemeColorOverride("caret_color", new Color(0.89f, 0.84f, 1f, 1f));
		textEdit.AddThemeColorOverride("selection_color", new Color(0.44f, 0.31f, 0.70f, 0.45f));
		textEdit.AddThemeColorOverride("font_readonly_color", new Color(0.72f, 0.70f, 0.82f, 0.6f));
	}
	
	public static void StyleOptionButton(OptionButton? optionButton)
	{
		if (optionButton == null) return;
		StylePrimaryButton(optionButton);
		optionButton.AddThemeColorOverride("font_color", TextColor);
		optionButton.AddThemeIconOverride("arrow", 
			optionButton.GetThemeIcon("arrow"));
	}

    // Single helper that keeps radius/border/content margins consistent for all controls.
    private static StyleBoxFlat CreateButtonStyle(Color background, Color border, int borderWidth)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            BorderColor = border,
            BorderBlend = true,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomRight = 12,
            CornerRadiusBottomLeft = 12,
            ContentMarginLeft = 10f,
            ContentMarginTop = 6f,
            ContentMarginRight = 10f,
            ContentMarginBottom = 6f,
        };
    }
    
    public static void StyleTabBar(TabBar? tabBar)
    {
	    if (tabBar == null) return;

	    var inactiveTab = new StyleBoxFlat
	    {
		    BgColor = new Color(0.18f, 0.14f, 0.27f, 0.75f),
		    BorderWidthBottom = 2,
		    BorderColor = new Color(0.64f, 0.54f, 0.82f, 0.3f),
		    CornerRadiusTopLeft = 12,
		    CornerRadiusTopRight = 12,
		    ContentMarginLeft = 12f,
		    ContentMarginRight = 12f,
		    ContentMarginTop = 6f,
		    ContentMarginBottom = 6f,
	    };

	    var activeTab = new StyleBoxFlat
	    {
		    BgColor = new Color(0.24f, 0.19f, 0.36f, 0.92f),
		    BorderWidthBottom = 2,
		    BorderColor = new Color(0.86f, 0.68f, 1f, 1f),
		    CornerRadiusTopLeft = 12,
		    CornerRadiusTopRight = 12,
		    ContentMarginLeft = 12f,
		    ContentMarginRight = 12f,
		    ContentMarginTop = 6f,
		    ContentMarginBottom = 6f,
	    };

	    var hoverTab = new StyleBoxFlat
	    {
		    BgColor = new Color(0.24f, 0.19f, 0.35f, 0.9f),
		    BorderWidthBottom = 2,
		    BorderColor = new Color(0.88f, 0.72f, 1f, 0.95f),
		    CornerRadiusTopLeft = 12,
		    CornerRadiusTopRight = 12,
		    ContentMarginLeft = 12f,
		    ContentMarginRight = 12f,
		    ContentMarginTop = 6f,
		    ContentMarginBottom = 6f,
	    };

	    tabBar.AddThemeStyleboxOverride("tab_unselected", inactiveTab);
	    tabBar.AddThemeStyleboxOverride("tab_selected", activeTab);
	    tabBar.AddThemeStyleboxOverride("tab_hovered", hoverTab);

	    tabBar.AddThemeColorOverride("font_selected_color", TextColor);
	    tabBar.AddThemeColorOverride("font_unselected_color", new Color(0.70f, 0.67f, 0.82f, 0.8f));
	    tabBar.AddThemeColorOverride("font_hovered_color", TextColor);
	    tabBar.AddThemeFontSizeOverride("font_size", 13);
    }
    
    // Helper for assigning this in other scenes
    public static void StyleGhostNav(params Button[] buttons)
    {
        foreach (var b in buttons)
            StyleGhostNavButton(b);
    }
    
    // Nav arrows transparent and animate on hover
    public static void StyleGhostNavButton(Button? button, float restingAlpha = 0.15f)
    {
        if (button == null) return;

        // Remove all backgrounds
        var empty = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", empty);
        button.AddThemeStyleboxOverride("hover", empty);
        button.AddThemeStyleboxOverride("pressed", empty);
        button.AddThemeStyleboxOverride("focus", empty);

        // Start semi transparent
        button.Modulate = new Color(0.78f, 0.7f, 0.8f, restingAlpha);

        // Prevent layout stretching
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        // Fix pivot after layout and on resize
        _ = FixPivotNextFrame(button);
        
        button.Resized += () =>
        {
            button.PivotOffset = button.Size / 2f;
        };

        ConnectGhostNavHover(button, restingAlpha);
    }
    
    // Connects the animation to mouse hover
    private static void ConnectGhostNavHover(Button button, float restingAlpha = 0.15f)
    {
        button.MouseEntered += () => AnimateGhostNav(button, true, restingAlpha);
        button.MouseExited += () => AnimateGhostNav(button, false, restingAlpha);
    }

    // Animates the arrows 
    private static void AnimateGhostNav(Button button, bool hovered, float restingAlpha = 0.15f)
    {
        var tween = button.CreateTween();
        tween.SetParallel(true);
        tween.SetTrans(Tween.TransitionType.Back);
        tween.SetEase(Tween.EaseType.Out);

        tween.TweenProperty(button, "scale",
            hovered ? new Vector2(1.3f, 1.3f) : Vector2.One, 0.2f);

        tween.TweenProperty(button, "modulate",
            hovered
                ? new Color(0.92f, 0.85f, 1f, 1f)  
                : new Color(0.78f, 0.7f, 0.8f, restingAlpha), 
            0.2f);
        GD.Print(button.PivotOffset, " vs ", button.Size);
    }
    
    // Drop shadow
    public static void ApplyDropShadow(Control control, 
	    float offsetX = 0f, float offsetY = 4f,
	    float blur = 2, Color? color = null)
    {
	    var shadowColor = color ?? new Color(0.01f, 0.01f, 0.03f, 0.36f);
	    
	    string[] states = { "normal", "hover", "pressed", "focus", "disabled", "normal_mirrored", "hover_mirrored" };
	    
	    if (control is Button button)
	    {
		    foreach (var state in states)
		    {
			    if (button.GetThemeStylebox(state) is not StyleBoxFlat flat)
				    continue;
			    var styled = (StyleBoxFlat)flat.Duplicate();
			    styled.ShadowColor = shadowColor;
			    styled.ShadowSize = (int)blur;
			    styled.ShadowOffset = new Vector2(offsetX, offsetY);
			    button.AddThemeStyleboxOverride(state, styled);
		    }
		    return;

	    }
	    if (control.GetThemeStylebox("panel") is StyleBoxFlat panelFlat)
	    {
		    var styled = (StyleBoxFlat)panelFlat.Duplicate();
		    styled.ShadowColor = shadowColor;
		    styled.ShadowSize = (int)blur;
		    styled.ShadowOffset = new Vector2(offsetX, offsetY);
		    control.AddThemeStyleboxOverride("panel", styled);
	    }
    }
    
    public static void ApplyParallaxShadow(Control control, float blur = 2f, float offsetY = 3f, Color? color = null)
    {
	    async void Apply()
	    {
		    await control.ToSignal(control.GetTree(), SceneTree.SignalName.ProcessFrame);
        
		    var screenWidth = control.GetViewport().GetVisibleRect().Size.X;
		    var centerX = control.GlobalPosition.X + control.Size.X * 0.5f;
		    var t = (centerX / screenWidth) * 2f - 1f; // -1 left, +1 right
		    
		    var offsetX = t * 3f;
        
		    ApplyDropShadow(control, offsetX, offsetY, blur, color);
	    }
	    Apply();
    }
    
    
    // Feedback on hover, wil adjust as needed
    public static void AddHoverFeedback(Button button, 
	    float scaleUp = 1.08f, float duration = 0.12f)
    
    {
	    
	    _ = FixPivotNextFrame(button);
	    button.Resized += () => button.PivotOffset = button.Size / 2f;
	    
	    Tween? activeTween = null;
	    
	    button.MouseEntered += () =>
	    {
		    AudioManager.Instance?.PlayButtonHover();
		    
		    activeTween?.Kill();
		    activeTween = button.CreateTween();
		    activeTween.SetTrans(Tween.TransitionType.Cubic);
		    activeTween.SetEase(Tween.EaseType.Out);
		    activeTween.TweenProperty(button, "scale", 
			    new Vector2(scaleUp, scaleUp), duration);
	    };

	    button.MouseExited += () =>
	    {
		    activeTween?.Kill();
		    activeTween = button.CreateTween();
		    activeTween.SetTrans(Tween.TransitionType.Elastic);
		    activeTween.SetEase(Tween.EaseType.InOut);
		    activeTween.TweenProperty(button, "scale", 
			    Vector2.One, 0.16f);
	    };

	    button.ButtonDown += () =>
	    {
		    activeTween?.Kill();
		    activeTween = button.CreateTween();
		    activeTween.SetTrans(Tween.TransitionType.Cubic);
		    activeTween.SetEase(Tween.EaseType.Out);
		    activeTween.TweenProperty(button, "scale",
			    new Vector2(scaleUp * 0.9f, scaleUp * 0.9f), 0.08f);
	    };

	    button.ButtonUp += () =>
	    {
		    activeTween?.Kill();
		    activeTween = button.CreateTween();
		    activeTween.SetTrans(Tween.TransitionType.Cubic);
		    activeTween.SetEase(Tween.EaseType.Out);
		    activeTween.TweenProperty(button, "scale", new Vector2(scaleUp, scaleUp), duration);
	    };
    }
    
    
    private static async System.Threading.Tasks.Task FixPivotNextFrame(Button button)
    {
        await button.ToSignal(button.GetTree(), SceneTree.SignalName.ProcessFrame);

        button.PivotOffset = button.Size / 2f;
    }
}
