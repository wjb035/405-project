using Godot;
using PGEmu.Services;
using System;

public partial class AppearanceSettings : Control
{
	[Export] public NodePath ThreeDButtonPath;
	[Export] public NodePath ListButtonPath;
	[Export] public NodePath GridButtonPath;
	[Export] public NodePath DescriptionTitlePath;
	[Export] public NodePath DescriptionBodyPath;
	[Export] public NodePath StatusPath;
	[Export] public NodePath ThreeDPreviewPath;
	[Export] public NodePath ListPreviewPath;
	[Export] public NodePath GridPreviewPath;




	private Button _threeDButton = null!;
	private Button _listButton = null!;
	private Button _gridButton = null!;
	private Label _descriptionTitle = null!;
	private Label _descriptionBody = null!;
	private Label _status = null!;
	private Control _threeDPreview = null!;
	private Control _listPreview = null!;
	private Control _gridPreview = null!;


	public override void _Ready()
	{
		_threeDButton = ResolveNode<Button>(ThreeDButtonPath, "Margin/Root/Modes/3DCarouselButton");
		_listButton = ResolveNode<Button>(ListButtonPath, "Margin/Root/Modes/ListButton");
		_gridButton = ResolveNode<Button>(GridButtonPath, "Margin/Root/Modes/GridButton");
		_descriptionTitle = ResolveNode<Label>(DescriptionTitlePath, "Margin/Root/Preview/Margin/PreviewRoot/CurrentLayout");
		_descriptionBody = ResolveNode<Label>(DescriptionBodyPath, "Margin/Root/Preview/Margin/PreviewRoot/CurrentDescription");
		_status = ResolveNode<Label>(StatusPath, "Margin/Root/Status");
		_threeDPreview = ResolveNode<Control>(ThreeDPreviewPath, "Margin/Root/Preview/Margin/PreviewRoot/PreviewSamples/ThreeDPreview");
		_listPreview = ResolveNode<Control>(ListPreviewPath, "Margin/Root/Preview/Margin/PreviewRoot/PreviewSamples/ListPreview");
		_gridPreview = ResolveNode<Control>(GridPreviewPath, "Margin/Root/Preview/Margin/PreviewRoot/PreviewSamples/GridPreview");



		ApplyThemeAesthetic();

		_listButton.Pressed += () => SaveLayout(BrowseLayoutMode.List);
		_gridButton.Pressed += () => SaveLayout(BrowseLayoutMode.Grid);
		_threeDButton.Pressed += () => SaveLayout(BrowseLayoutMode.ThreeD);
		
		UpdateUi(BrowseLayoutSettings.GetLayout(), announceSave: false);
	}

	private void SaveLayout(BrowseLayoutMode layout)
	{
		AudioManager.Instance?.PlaySelect();
		BrowseLayoutSettings.SetLayout(layout);
		UpdateUi(layout, announceSave: true);
	}

	private void UpdateUi(BrowseLayoutMode layout, bool announceSave)
	{
		if (layout == BrowseLayoutMode.Carousel)
			layout = BrowseLayoutMode.ThreeD;

		_listButton.ButtonPressed = layout == BrowseLayoutMode.List;
		_gridButton.ButtonPressed = layout == BrowseLayoutMode.Grid;
		_threeDButton.ButtonPressed   = layout == BrowseLayoutMode.ThreeD; 
		
		_descriptionTitle.Text = BrowseLayoutSettings.GetLabel(layout);
		_descriptionBody.Text = layout switch
		{
			BrowseLayoutMode.List =>
				"A more compact layout.",
			BrowseLayoutMode.Grid =>
				"A wall of larger tiles, built for couch browsing, and quick pick-up play.",
			BrowseLayoutMode.ThreeD => "A 3D shelf of animated game cases.",
			_ =>
				"A 3D shelf of animated game cases.",
		};

		_threeDPreview.Visible = layout == BrowseLayoutMode.ThreeD;
		_listPreview.Visible = layout == BrowseLayoutMode.List;
		_gridPreview.Visible = layout == BrowseLayoutMode.Grid;

		_status.Text = announceSave
			? $"Saved {BrowseLayoutSettings.GetLabel(layout).ToLowerInvariant()} for library browsing."
			: "Choose how the game library should look when you open a platform.";
	}

	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var title = GetNodeOrNull<Label>("Margin/Root/Title");
		var hint = GetNodeOrNull<Label>("Margin/Root/Hint");
		var previewEyebrow = GetNodeOrNull<Label>("Margin/Root/Preview/Margin/PreviewRoot/PreviewEyebrow");
		UiStyle.StyleTitleLabel(title);
		UiStyle.StyleMetaLabel(hint);
		UiStyle.StyleMetaLabel(previewEyebrow);

		StyleModeButton(_listButton);
		StyleModeButton(_gridButton);
		StyleModeButton(_threeDButton); 
		
		var preview = GetNodeOrNull<PanelContainer>("Margin/Root/Preview");
		if (preview != null)
		{
			preview.AddThemeStyleboxOverride("panel", new StyleBoxFlat
			{
				BgColor = new Color(0.11f, 0.09f, 0.18f, 0.96f),
				BorderColor = new Color(0.68f, 0.56f, 0.90f, 0.92f),
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 22,
				CornerRadiusTopRight = 22,
				CornerRadiusBottomRight = 22,
				CornerRadiusBottomLeft = 22,
				ContentMarginLeft = 22,
				ContentMarginTop = 22,
				ContentMarginRight = 22,
				ContentMarginBottom = 22,
			});
		}

		UiStyle.StyleTitleLabel(_descriptionTitle);
		UiStyle.StyleMetaLabel(_descriptionBody);
		_descriptionBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		UiStyle.StyleStatusLabel(_status);

		var sidebar = GetNodeOrNull<PanelContainer>("Margin/Root/Sidebar");
		if (sidebar != null)
		{
			sidebar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
			{
				BgColor = new Color(0.15f, 0.12f, 0.24f, 0.94f),
				BorderColor = new Color(0.63f, 0.50f, 0.84f, 0.85f),
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 22,
				CornerRadiusTopRight = 22,
				CornerRadiusBottomRight = 22,
				CornerRadiusBottomLeft = 22,
				ContentMarginLeft = 18,
				ContentMarginTop = 18,
				ContentMarginRight = 18,
				ContentMarginBottom = 18,
			});
		}
	}

	private T ResolveNode<T>(NodePath exportedPath, string fallbackPath) where T : Node
	{
		if (!string.IsNullOrWhiteSpace(exportedPath?.ToString()))
		{
			var fromExport = GetNodeOrNull<T>(exportedPath);
			if (fromExport != null)
				return fromExport;
		}

		var fromFallback = GetNodeOrNull<T>(fallbackPath);
		if (fromFallback != null)
			return fromFallback;

		throw new InvalidOperationException($"Couldn't resolve {typeof(T).Name} for '{fallbackPath}'.");
	}

	private static void StyleModeButton(Button button)
	{
		button.ToggleMode = true;
		UiStyle.StylePrimaryButton(button);
		button.CustomMinimumSize = new Vector2(180f, 54f);
	}
}
