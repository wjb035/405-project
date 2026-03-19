using Godot;
using PGEmu.Services;

public partial class AppearanceSettings : Control
{
    [Export] public NodePath CarouselButtonPath;
    [Export] public NodePath ListButtonPath;
    [Export] public NodePath GridButtonPath;
    [Export] public NodePath DescriptionTitlePath;
    [Export] public NodePath DescriptionBodyPath;
    [Export] public NodePath StatusPath;
    [Export] public NodePath CarouselPreviewPath;
    [Export] public NodePath ListPreviewPath;
    [Export] public NodePath GridPreviewPath;

    private Button _carouselButton = null!;
    private Button _listButton = null!;
    private Button _gridButton = null!;
    private Label _descriptionTitle = null!;
    private Label _descriptionBody = null!;
    private Label _status = null!;
    private Control _carouselPreview = null!;
    private Control _listPreview = null!;
    private Control _gridPreview = null!;

    public override void _Ready()
    {
        _carouselButton = GetNode<Button>(CarouselButtonPath);
        _listButton = GetNode<Button>(ListButtonPath);
        _gridButton = GetNode<Button>(GridButtonPath);
        _descriptionTitle = GetNode<Label>(DescriptionTitlePath);
        _descriptionBody = GetNode<Label>(DescriptionBodyPath);
        _status = GetNode<Label>(StatusPath);
        _carouselPreview = GetNode<Control>(CarouselPreviewPath);
        _listPreview = GetNode<Control>(ListPreviewPath);
        _gridPreview = GetNode<Control>(GridPreviewPath);

        ApplyThemeAesthetic();

        _carouselButton.Pressed += () => SaveLayout(BrowseLayoutMode.Carousel);
        _listButton.Pressed += () => SaveLayout(BrowseLayoutMode.List);
        _gridButton.Pressed += () => SaveLayout(BrowseLayoutMode.Grid);

        UpdateUi(BrowseLayoutSettings.GetLayout(), announceSave: false);
    }

    private void SaveLayout(BrowseLayoutMode layout)
    {
        BrowseLayoutSettings.SetLayout(layout);
        UpdateUi(layout, announceSave: true);
    }

    private void UpdateUi(BrowseLayoutMode layout, bool announceSave)
    {
        _carouselButton.ButtonPressed = layout == BrowseLayoutMode.Carousel;
        _listButton.ButtonPressed = layout == BrowseLayoutMode.List;
        _gridButton.ButtonPressed = layout == BrowseLayoutMode.Grid;

        _descriptionTitle.Text = BrowseLayoutSettings.GetLabel(layout);
        _descriptionBody.Text = layout switch
        {
            BrowseLayoutMode.List =>
                "A denser library browser with ranked rows, quick status chips, and clear scan-and-launch hierarchy.",
            BrowseLayoutMode.Grid =>
                "A channel-style wall of rounded tiles built for couch browsing, directional pad movement, and quick pick-up play.",
            _ =>
                "The oversized center-weighted carousel with the original PGEmu motion and big-selection feel.",
        };

        _carouselPreview.Visible = layout == BrowseLayoutMode.Carousel;
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
        var previewEyebrow = GetNodeOrNull<Label>("Margin/Root/Stage/StageMargin/StageRoot/PreviewEyebrow");
        UiStyle.StyleTitleLabel(title);
        UiStyle.StyleMetaLabel(hint);
        UiStyle.StyleMetaLabel(previewEyebrow);

        StyleModeButton(_carouselButton);
        StyleModeButton(_listButton);
        StyleModeButton(_gridButton);

        var preview = GetNodeOrNull<PanelContainer>("Margin/Root/Stage");
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

    private static void StyleModeButton(Button button)
    {
        button.ToggleMode = true;
        UiStyle.StylePrimaryButton(button);
        button.CustomMinimumSize = new Vector2(180f, 54f);
    }
}
