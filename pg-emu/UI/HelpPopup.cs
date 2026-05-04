using Godot;
using PGEmu.Services;
using System.Collections.Generic;

public partial class HelpPopup : PopupPanel
{
	private Panel _content = null!;
	private VBoxContainer _list = null!;

	private static readonly IReadOnlyList<(string Title, string Body)> HelpItems = new[]
	{
		(
			"Finding games",
			"Use the console carousel to pick a platform, then press Play to open its games. The search bar can filter by games or users from the home screen."
		),
		(
			"Profiles and themes",
			"Open Profile from the top bar, then Customize to edit your username, bio, avatar, frame, and public profile theme."
		),
		(
			"Friends and chat",
			"Use the profile icon to find people, send friend requests, and open friend profiles. Chat messages and friend requests appear in the inbox."
		),
		(
			"Collections",
			"Collections let you group games into shelves. Open Collections from the folder icon or from a game card."
		),
		(
			"Achievements",
			"Add your RetroAchievements username and API key in Settings. Achievement pages use that connection to load trophies."
		),
		(
			"Controls",
			"Keyboard, mouse, and controller navigation are supported. Use the back button or controller back action to leave most screens."
		)
	};

	public override void _Ready()
	{
		Size = new Vector2I(430, 560);
		Hide();
		BuildContent();
	}

	public void ShowPopup()
	{
		AudioManager.Instance?.PlayMessageOpen();
		Position = new Vector2I(680, 60);
		Popup();
		GrabFocus();

		_content.Scale = new Vector2(0.8f, 0.8f);
		_content.Modulate = new Color(1f, 1f, 1f, 0f);

		var tween = CreateTween();
		tween.TweenProperty(_content, "scale", Vector2.One, 0.1f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
		tween.TweenProperty(_content, "modulate:a", 1f, 0.1f);
	}

	public void HidePopup()
	{
		var tween = CreateTween();
		tween.TweenProperty(_content, "scale", new Vector2(0.8f, 0.8f), 0.15f)
			.SetEase(Tween.EaseType.In)
			.SetTrans(Tween.TransitionType.Back);
		tween.TweenProperty(_content, "modulate:a", 0f, 0.15f)
			.SetEase(Tween.EaseType.In);
		tween.Finished += Hide;
	}

	private void BuildContent()
	{
		AddThemeStyleboxOverride("panel", TransparentPanel());

		_content = new Panel
		{
			CustomMinimumSize = new Vector2(430, 560),
			Size = new Vector2(430, 560)
		};
		_content.AddThemeStyleboxOverride("panel", LoadPanelStyle());
		AddChild(_content);

		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 14);
		_content.AddChild(margin);

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 10);
		margin.AddChild(root);

		var header = new HBoxContainer();
		header.AddThemeConstantOverride("separation", 10);
		root.AddChild(header);

		var title = new Label
		{
			Text = "Help",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		UiStyle.StyleTitleLabel(title);
		title.AddThemeFontSizeOverride("font_size", 24);
		header.AddChild(title);

		var close = new Button
		{
			Text = "Close",
			CustomMinimumSize = new Vector2(82, 32)
		};
		UiStyle.StyleTopBarButton(close);
		UiStyle.AddHoverFeedback(close);
		UiStyle.ApplyParallaxShadow(close);
		close.Pressed += HidePopup;
		header.AddChild(close);

		var subtitle = new Label
		{
			Text = "FAQs and useful info",
			AutowrapMode = TextServer.AutowrapMode.Word
		};
		UiStyle.StyleMetaLabel(subtitle);
		root.AddChild(subtitle);

		var separator = new HSeparator();
		root.AddChild(separator);

		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
		};
		root.AddChild(scroll);

		_list = new VBoxContainer();
		_list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_list.AddThemeConstantOverride("separation", 9);
		scroll.AddChild(_list);

		foreach (var item in HelpItems)
			AddHelpItem(item.Title, item.Body);
	}

	private void AddHelpItem(string title, string body)
	{
		var card = new PanelContainer();
		card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		card.AddThemeStyleboxOverride("panel", CardStyle());
		_list.AddChild(card);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		card.AddChild(margin);

		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 4);
		margin.AddChild(stack);

		var heading = new Label { Text = title };
		UiStyle.StyleMetaLabel(heading);
		heading.AddThemeColorOverride("font_color", new Color(0.78f, 0.68f, 1f, 1f));
		stack.AddChild(heading);

		var text = new Label
		{
			Text = body,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.Word
		};
		UiStyle.StyleStatusLabel(text);
		stack.AddChild(text);
	}

	private static StyleBoxFlat TransparentPanel()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0f),
			CornerRadiusTopLeft = 7,
			CornerRadiusTopRight = 7,
			CornerRadiusBottomLeft = 7,
			CornerRadiusBottomRight = 7
		};
	}

	private static StyleBox LoadPanelStyle()
	{
		return GD.Load<StyleBox>("res://inbox_style_flat.tres") ?? CardStyle();
	}

	private static StyleBoxFlat CardStyle()
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.13f, 0.11f, 0.22f, 0.88f),
			BorderColor = new Color(0.62f, 0.54f, 0.86f, 0.36f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 12,
			CornerRadiusTopRight = 12,
			CornerRadiusBottomLeft = 12,
			CornerRadiusBottomRight = 12
		};
	}
}
