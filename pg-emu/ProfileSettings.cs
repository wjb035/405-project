using Godot;
using System;
using System.Collections.Generic;
using PGEmu.Services;


public partial class ProfileSettings : Control
{
	[Export] NodePath UsernameEditPath;
	[Export] NodePath UsernameSaveButtonPath;
	[Export] NodePath BioEditPath;
	[Export] NodePath BioSaveButtonPath;
	[Export] NodePath AvatarSaveButtonPath;
	private LineEdit _usernameEdit;
	private Button _usernameSave;
	private LineEdit _bioEdit;
	private Button _bioSave;
	private Button _avatarSave;
	private Label _status = null!;
	private PanelContainer _visualStylePanel = null!;
	private PanelContainer _previewCard = null!;
	private PanelContainer _previewAvatar = null!;
	private Label _previewName = null!;
	private Label _previewBio = null!;
	private OptionButton _backgroundOption = null!;
	private OptionButton _frameOption = null!;
	private Button _saveStyle = null!;
	private Button _resetStyle = null!;
	private readonly Dictionary<string, Button> _accentButtons = new(StringComparer.OrdinalIgnoreCase);
	private string _selectedAccentId = ProfileVisualStyleCatalog.DefaultAccentId;
	private string _selectedAvatarFrameId = ProfileVisualStyleCatalog.DefaultAvatarFrameId;
	private string _selectedBackgroundId = ProfileVisualStyleCatalog.DefaultBackgroundId;
	ProfileService _profileService = new ProfileService();

	private FileDialog _fileDialog;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_usernameEdit = GetNode<LineEdit>(UsernameEditPath);
		_usernameSave = GetNode<Button>(UsernameSaveButtonPath);
		_bioEdit = GetNode<LineEdit>(BioEditPath);
		_bioSave = GetNode<Button>(BioSaveButtonPath);
		_avatarSave = GetNode<Button>(AvatarSaveButtonPath);
		_status = GetNode<Label>("Margin/Root/Status");
		_visualStylePanel = GetNode<PanelContainer>("Margin/Root/VisualStylePanel");
		_previewCard = GetNode<PanelContainer>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/PreviewCard");
		_previewAvatar = GetNode<PanelContainer>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/PreviewCard/PreviewMargin/PreviewRow/PreviewAvatar");
		_previewName = GetNode<Label>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/PreviewCard/PreviewMargin/PreviewRow/PreviewText/PreviewName");
		_previewBio = GetNode<Label>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/PreviewCard/PreviewMargin/PreviewRow/PreviewText/PreviewBio");
		_backgroundOption = GetNode<OptionButton>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/BackgroundRow/BackgroundOption");
		_frameOption = GetNode<OptionButton>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/FrameRow/FrameOption");
		_saveStyle = GetNode<Button>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/StyleActions/SaveStyle");
		_resetStyle = GetNode<Button>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/StyleActions/ResetStyle");

		// File picker for avatar
		_fileDialog = new FileDialog();
		_fileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
		_fileDialog.Access = FileDialog.AccessEnum.Filesystem;
		_fileDialog.Filters = ["*.png,*.jpg,*.jpeg,*.webp,*.gif ; Images"];
		_fileDialog.FileSelected += OnAvatarFileSelected;
		AddChild(_fileDialog);

		SetupStyleControls();
		ApplyThemeAesthetic();
	
		_usernameSave.Pressed += ChangeUsername;
		_bioSave.Pressed += ChangeBio;
		_avatarSave.Pressed += ChangeAvatar;
		_saveStyle.Pressed += SaveProfileStyle;
		_resetStyle.Pressed += ResetVisualStyle;
		_usernameEdit.TextChanged += _ => UpdateStylePreview();
		_bioEdit.TextChanged += _ => UpdateStylePreview();

		_ = LoadProfileStyleAsync();

	}
	
	public void ChangeUsername() 
	{
		AudioManager.Instance?.PlaySelect();
		
		GD.Print(_usernameEdit.Text?.Trim());
		_profileService.SetUsername(_usernameEdit.Text?.Trim());
		_status.Text = "Username updated.";
	}
	
	public void ChangeBio() 
	{
		AudioManager.Instance?.PlaySelect();
		
		GD.Print(_bioEdit.Text?.Trim());
		_profileService.SetBio(_bioEdit.Text?.Trim());
		_status.Text = "Bio updated.";
	}
	
	public void ChangeAvatar() 
	{
		AudioManager.Instance?.PlaySelect();
		
		_fileDialog.PopupCentered(new Vector2I(800, 600));
	}

	private async void OnAvatarFileSelected(string path)
	{
		_status.Text = "Uploading avatar...";
		var success = await _profileService.SetAvatar(path);
		_status.Text = success ? "Avatar updated." : "Avatar upload failed.";
	}

	private async void SaveProfileStyle()
	{
		AudioManager.Instance?.PlaySelect();

		_status.Text = "Saving profile theme...";
		var success = await _profileService.SetProfileStyleAsync(_selectedAccentId, _selectedAvatarFrameId, _selectedBackgroundId);
		_status.Text = success ? "Profile theme updated." : BuildFailureStatus("Profile theme update failed.");
	}

	private void ResetVisualStyle()
	{
		AudioManager.Instance?.PlaySelect();

		SelectVisualStyle(
			ProfileVisualStyleCatalog.DefaultAccentId,
			ProfileVisualStyleCatalog.DefaultAvatarFrameId,
			ProfileVisualStyleCatalog.DefaultBackgroundId,
			updateControls: true);
		_status.Text = "Default theme selected.";
	}

	private async System.Threading.Tasks.Task LoadProfileStyleAsync()
	{
		var profile = await _profileService.GetMyProfile();
		if (profile == null || !GodotObject.IsInstanceValid(this) || !IsInsideTree())
			return;

		_usernameEdit.Text = profile.Username ?? string.Empty;
		_bioEdit.Text = profile.Bio ?? string.Empty;
		SelectVisualStyle(profile.ProfileAccent, profile.AvatarFrame, profile.ProfileBackground, updateControls: true);
		_status.Text = "";
	}

	private void SetupStyleControls()
	{
		_accentButtons.Clear();
		if (GetNodeOrNull<Label>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/AccentLabel") is Label accentLabel)
			accentLabel.Visible = false;
		if (GetNodeOrNull<Control>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/AccentSwatches") is Control accentSwatches)
			accentSwatches.Visible = false;
		if (GetNodeOrNull<Label>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/BackgroundRow/BackgroundLabel") is Label themeLabel)
			themeLabel.Text = "Theme";

		_backgroundOption.Clear();
		for (int index = 0; index < ProfileVisualStyleCatalog.Backgrounds.Count; index++)
		{
			var background = ProfileVisualStyleCatalog.Backgrounds[index];
			_backgroundOption.AddItem(background.Label, index);
		}
		_backgroundOption.ItemSelected += OnBackgroundSelected;

		_frameOption.Clear();
		for (int index = 0; index < ProfileVisualStyleCatalog.AvatarFrames.Count; index++)
		{
			var frame = ProfileVisualStyleCatalog.AvatarFrames[index];
			_frameOption.AddItem(frame.Label, index);
		}
		_frameOption.ItemSelected += OnFrameSelected;

		SelectVisualStyle(
			ProfileVisualStyleCatalog.DefaultAccentId,
			ProfileVisualStyleCatalog.DefaultAvatarFrameId,
			ProfileVisualStyleCatalog.DefaultBackgroundId,
			updateControls: true);
	}

	private void SelectAccent(string accentId)
	{
		AudioManager.Instance?.PlaySelect();
		SelectVisualStyle(ProfileVisualStyleCatalog.DefaultAccentId, _selectedAvatarFrameId, _selectedBackgroundId, updateControls: false);
	}

	private void OnBackgroundSelected(long index)
	{
		if (index < 0 || index >= ProfileVisualStyleCatalog.Backgrounds.Count)
			return;

		_selectedBackgroundId = ProfileVisualStyleCatalog.Backgrounds[(int)index].Id;
		UpdateStylePreview();
	}

	private void OnFrameSelected(long index)
	{
		if (index < 0 || index >= ProfileVisualStyleCatalog.AvatarFrames.Count)
			return;

		_selectedAvatarFrameId = ProfileVisualStyleCatalog.AvatarFrames[(int)index].Id;
		UpdateStylePreview();
	}

	private void SelectVisualStyle(string? accentId, string? avatarFrameId, string? backgroundId, bool updateControls)
	{
		var resolved = ProfileVisualStyleCatalog.Resolve(accentId, avatarFrameId, backgroundId);
		_selectedAccentId = ProfileVisualStyleCatalog.DefaultAccentId;
		_selectedAvatarFrameId = resolved.AvatarFrame.Id;
		_selectedBackgroundId = resolved.Background.Id;

		if (updateControls)
		{
			_backgroundOption.Select(ProfileVisualStyleCatalog.FindBackgroundIndex(_selectedBackgroundId));
			_frameOption.Select(ProfileVisualStyleCatalog.FindAvatarFrameIndex(_selectedAvatarFrameId));
		}

		UpdateStylePreview();
	}

	private void UpdateStylePreview()
	{
		var style = ProfileVisualStyleCatalog.Resolve(_selectedAccentId, _selectedAvatarFrameId, _selectedBackgroundId);
		var background = style.Background;
		var accent = background.Accent;

		if (GetNodeOrNull<ColorRect>("Bg") is ColorRect bg)
			bg.Color = new Color(background.Overlay.R, background.Overlay.G, background.Overlay.B, 1f);

		_visualStylePanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(background.CardSurface, WithAlpha(accent, 0.38f), 16, 1));
		_previewCard.AddThemeStyleboxOverride("panel", CreatePanelStyle(background.CardSurfaceAlt, WithAlpha(accent, 0.62f), 14, 1));
		_previewAvatar.AddThemeStyleboxOverride("panel", CreatePanelStyle(Mix(background.CardSurfaceInset, accent, 0.12f), accent, style.AvatarFrame.PanelRadius, 2));

		_previewName.Text = string.IsNullOrWhiteSpace(_usernameEdit.Text) ? "Player" : _usernameEdit.Text.Trim();
		_previewBio.Text = string.IsNullOrWhiteSpace(_bioEdit.Text) ? "No bio yet." : _bioEdit.Text.Trim();
		_previewName.AddThemeColorOverride("font_color", new Color(accent.R, accent.G, accent.B, 0.98f));
		_previewBio.AddThemeColorOverride("font_color", new Color(0.88f, 0.86f, 0.96f, 0.88f));

		foreach (var accentPreset in ProfileVisualStyleCatalog.Accents)
		{
			if (!_accentButtons.TryGetValue(accentPreset.Id, out var button))
				continue;

			var selected = string.Equals(accentPreset.Id, _selectedAccentId, StringComparison.OrdinalIgnoreCase);
			ApplyAccentButtonStyle(button, accentPreset.Primary, selected);
		}
	}
	
	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var title = GetNodeOrNull<Label>("Margin/Root/Title");
		UiStyle.StyleTitleLabel(title);

		var usernameLabel = GetNodeOrNull<Label>("Margin/Root/ChangeUsernameLabel");
		var bioLabel = GetNodeOrNull<Label>("Margin/Root/ChangeBioLabel3");
		var avatarLabel = GetNodeOrNull<Label>("Margin/Root/ChangeAvatarLabel2");
		var visualStyleLabel = GetNodeOrNull<Label>("Margin/Root/VisualStyleLabel");
		var accentLabel = GetNodeOrNull<Label>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/AccentLabel");
		var backgroundLabel = GetNodeOrNull<Label>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/BackgroundRow/BackgroundLabel");
		var frameLabel = GetNodeOrNull<Label>("Margin/Root/VisualStylePanel/MarginContainer/VBoxContainer/FrameRow/FrameLabel");
		UiStyle.StyleMetaLabel(usernameLabel);
		UiStyle.StyleMetaLabel(bioLabel);
		UiStyle.StyleMetaLabel(avatarLabel);
		UiStyle.StyleMetaLabel(visualStyleLabel);
		UiStyle.StyleMetaLabel(accentLabel);
		UiStyle.StyleMetaLabel(backgroundLabel);
		UiStyle.StyleMetaLabel(frameLabel);

		UiStyle.StyleLineEdit(_usernameEdit);
		UiStyle.StyleLineEdit(_bioEdit);
		_usernameEdit.PlaceholderText = "New username";
		_bioEdit.PlaceholderText = "New bio";

		UiStyle.StylePrimaryButton(_usernameSave);
		UiStyle.AddHoverFeedback(_usernameSave);
		UiStyle.ApplyParallaxShadow(_usernameSave);

		UiStyle.StylePrimaryButton(_bioSave);
		UiStyle.AddHoverFeedback(_bioSave);
		UiStyle.ApplyParallaxShadow(_bioSave);

		UiStyle.StylePrimaryButton(_avatarSave);
		UiStyle.AddHoverFeedback(_avatarSave);
		UiStyle.ApplyParallaxShadow(_avatarSave);
		UiStyle.StylePrimaryButton(_saveStyle);
		UiStyle.AddHoverFeedback(_saveStyle);
		UiStyle.ApplyParallaxShadow(_saveStyle);
		UiStyle.StylePrimaryButton(_resetStyle);
		UiStyle.AddHoverFeedback(_resetStyle);
		UiStyle.ApplyParallaxShadow(_resetStyle);
		UiStyle.StyleOptionButton(_backgroundOption);
		UiStyle.StyleOptionButton(_frameOption);
		UiStyle.StylePopupMenu(_backgroundOption.GetPopup());
		UiStyle.StylePopupMenu(_frameOption.GetPopup());
		UiStyle.TightenButtonContentPadding(_usernameSave, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_bioSave, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_avatarSave, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_saveStyle, horizontal: 6f, vertical: 2f);
		UiStyle.TightenButtonContentPadding(_resetStyle, horizontal: 6f, vertical: 2f);


		UiStyle.StyleStatusLabel(_status);
		_status.Text = "";
		UpdateStylePreview();
	}

	private string BuildFailureStatus(string fallback)
	{
		var auth = AuthService.Instance;
		return auth != null && !string.IsNullOrWhiteSpace(auth.LastErrorMessage)
			? auth.LastErrorMessage
			: fallback;
	}

	private static void ApplyAccentButtonStyle(Button button, Color accent, bool selected)
	{
		var background = new Color(accent.R * 0.32f, accent.G * 0.32f, accent.B * 0.32f, 0.92f);
		var border = selected ? new Color(0.98f, 0.96f, 1f, 1f) : new Color(accent.R, accent.G, accent.B, 0.72f);
		var borderWidth = selected ? 3 : 1;
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, border, borderWidth, 999, 10f, 4f));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(Mix(background, accent, 0.18f), accent, borderWidth, 999, 10f, 4f));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(Mix(background, accent, 0.08f), accent, borderWidth, 999, 10f, 4f));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(Mix(background, accent, 0.18f), new Color(0.98f, 0.96f, 1f, 1f), 3, 999, 10f, 4f));
		button.AddThemeColorOverride("font_color", new Color(0.98f, 0.96f, 1f, 0.98f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.98f, 0.96f, 1f, 0.98f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.98f, 0.96f, 1f, 0.98f));
		button.AddThemeColorOverride("font_focus_color", new Color(0.98f, 0.96f, 1f, 0.98f));
	}

	private static StyleBoxFlat CreatePanelStyle(Color background, Color border, int radius, int borderWidth)
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
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomRight = radius,
			CornerRadiusBottomLeft = radius
		};
	}

	private static StyleBoxFlat CreateButtonStyle(Color background, Color border, int borderWidth, int radius, float horizontalPadding, float verticalPadding)
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
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomRight = radius,
			CornerRadiusBottomLeft = radius,
			ContentMarginLeft = horizontalPadding,
			ContentMarginTop = verticalPadding,
			ContentMarginRight = horizontalPadding,
			ContentMarginBottom = verticalPadding
		};
	}

	private static Color WithAlpha(Color color, float alpha)
	{
		return new Color(color.R, color.G, color.B, alpha);
	}

	private static Color Mix(Color from, Color to, float amount)
	{
		return new Color(
			from.R + ((to.R - from.R) * amount),
			from.G + ((to.G - from.G) * amount),
			from.B + ((to.B - from.B) * amount),
			from.A + ((to.A - from.A) * amount));
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
