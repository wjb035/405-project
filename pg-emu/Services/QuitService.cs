using Godot;

namespace PGEmu.Services;

// Global quit confirmation overlay that listens for Escape on every scene.
public partial class QuitService : CanvasLayer
{
	private PopupPanel _quitPopup = null!;
	private Panel _popupContent = null!;
	private Button _exitButton = null!;
	private Button _stayButton = null!;
	private Tween? _popupTween;

	public override void _Ready()
	{
		// Keep this service alive/interactive regardless of the active scene tree.
		ProcessMode = ProcessModeEnum.Always;
		Layer = 100;
		BuildPopup();
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsEscapePressed(@event))
			return;

		GetViewport()?.SetInputAsHandled();

		// Second Escape confirms quit when the dialog is already open.
		if (_quitPopup.Visible)
		{
			QuitNow();
			return;
		}

		ShowPopup();
	}

	private static bool IsEscapePressed(InputEvent @event)
	{
		return @event is InputEventKey keyEvent &&
			   keyEvent.Pressed &&
			   !keyEvent.Echo &&
			   keyEvent.Keycode == Key.Escape;
	}

	private void BuildPopup()
	{
		// Build the popup tree in code so we can autoload this service without a scene file.
		_quitPopup = new PopupPanel
		{
			Visible = false,
			Size = new Vector2I(420, 180),
		};
		_quitPopup.AddThemeStyleboxOverride("panel", CreateTransparentPopupStyle());
		AddChild(_quitPopup);

		_popupContent = new Panel();
		_popupContent.AnchorRight = 1f;
		_popupContent.AnchorBottom = 1f;
		_popupContent.OffsetLeft = 0f;
		_popupContent.OffsetTop = 0f;
		_popupContent.OffsetRight = 0f;
		_popupContent.OffsetBottom = 0f;

		var contentStyle = GD.Load<StyleBox>("res://inbox_style_flat.tres");
		if (contentStyle != null)
			_popupContent.AddThemeStyleboxOverride("panel", contentStyle);

		_quitPopup.AddChild(_popupContent);

		var margin = new MarginContainer();
		margin.AnchorRight = 1f;
		margin.AnchorBottom = 1f;
		margin.OffsetLeft = 16f;
		margin.OffsetTop = 14f;
		margin.OffsetRight = -16f;
		margin.OffsetBottom = -14f;
		_popupContent.AddChild(margin);

		var root = new VBoxContainer();
		root.AnchorRight = 1f;
		root.AnchorBottom = 1f;
		root.AddThemeConstantOverride("separation", 10);
		margin.AddChild(root);

		var title = new Label
		{
			Text = "Exit PGEmu?",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.95f));
		title.AddThemeFontSizeOverride("font_size", 22);
		UiStyle.StyleTitleLabel(title);

		root.AddChild(title);

		var body = new Label
		{
			Text = "Are you sure you want to quit PGEmu?",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		body.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
		body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		UiStyle.StyleStatusLabel(body);
		root.AddChild(body);

		var buttons = new HBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		buttons.AddThemeConstantOverride("separation", 8);
		root.AddChild(buttons);

		_stayButton = new Button
		{
			Text = "Stay",
			CustomMinimumSize = new Vector2(96, 34),
		};
		UiStyle.StylePrimaryButton(_stayButton);
		UiStyle.AddHoverFeedback(_stayButton);
		UiStyle.ApplyParallaxShadow(_stayButton);
		buttons.AddChild(_stayButton);

		_exitButton = new Button
		{
			Text = "Exit",
			CustomMinimumSize = new Vector2(96, 34),
		};
		UiStyle.StylePrimaryButton(_exitButton);
		UiStyle.AddHoverFeedback(_exitButton);
		UiStyle.ApplyParallaxShadow(_exitButton);
		buttons.AddChild(_exitButton);

		_stayButton.Pressed += HidePopup;
		_exitButton.Pressed += QuitNow;
	}

	private static StyleBoxFlat CreateTransparentPopupStyle()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.6f, 0.6f, 0.6f, 0f),
			CornerRadiusTopLeft = 7,
			CornerRadiusTopRight = 7,
			CornerRadiusBottomRight = 7,
			CornerRadiusBottomLeft = 7,
		};
	}

	private void ShowPopup()
	{
		_popupTween?.Kill();

		// Reset state before animating in.
		_quitPopup.PopupCentered(_quitPopup.Size);
		_popupContent.PivotOffset = _popupContent.Size * 0.5f;
		_popupContent.Scale = new Vector2(0.8f, 0.8f);
		_popupContent.Modulate = new Color(1f, 1f, 1f, 0f);

		_popupTween = CreateTween();
		_popupTween.TweenProperty(_popupContent, "scale", new Vector2(1f, 1f), 0.1f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
		_popupTween.TweenProperty(_popupContent, "modulate:a", 1f, 0.1f);

		_stayButton.GrabFocus();
	}

	private void HidePopup()
	{
		AudioManager.Instance?.PlayClick();

		if (!_quitPopup.Visible)
			return;

		// Animate out, then hide to keep focus/input clean.
		_popupTween?.Kill();
		_popupTween = CreateTween();
		_popupTween.TweenProperty(_popupContent, "scale", new Vector2(0.8f, 0.8f), 0.15f)
			.SetEase(Tween.EaseType.In)
			.SetTrans(Tween.TransitionType.Back);
		_popupTween.TweenProperty(_popupContent, "modulate:a", 0f, 0.15f)
			.SetEase(Tween.EaseType.In);
		_popupTween.Finished += () => _quitPopup.Hide();
	}

	private void QuitNow()
	{
		AudioManager.Instance?.PlaySelect();
		GetTree().Quit();
	}
}
