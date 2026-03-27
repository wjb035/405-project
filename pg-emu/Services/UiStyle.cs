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

	public static void StyleOptionButton(OptionButton? optionButton)
	{
		if (optionButton == null) return;
		StylePrimaryButton(optionButton);
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
}
