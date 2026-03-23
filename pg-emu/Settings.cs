using Godot;
using System;
using PGEmu.Services;

public partial class Settings : Control
{
	[Export] public NodePath BackPath;
	[Export] public NodePath ProfileChoicePath;
	[Export] public NodePath ProfileScreenPath;
	[Export] public NodePath VaultChoicePath;
	[Export] public NodePath VaultScreenPath;
	[Export] public NodePath AppearanceChoicePath;
	[Export] public NodePath AppearanceScreenPath;
	[Export] public NodePath AchievementChoicePath;
	[Export] public NodePath AchievementScreenPath;
	
	
	private Control _profileScreen;
	private Control _vaultScreen;
	private Control _appearanceScreen;
	private Control _achievementScreen;
	
	private Button _back;
	private Button _profileChoice;
	private Button _vaultChoice;
	private Button _appearanceChoice;
	private Button _achievementChoice;
	
	
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		_profileScreen = GetNode<Control>(ProfileScreenPath);
		_vaultScreen = GetNode<Control>(VaultScreenPath);
		_appearanceScreen = GetNode<Control>(AppearanceScreenPath);
		_achievementScreen = GetNode<Control>(AchievementScreenPath);
		_profileChoice = GetNode<Button>(ProfileChoicePath);
		_vaultChoice = GetNode<Button>(VaultChoicePath);
		_appearanceChoice = GetNode<Button>(AppearanceChoicePath);
		_achievementChoice = GetNode<Button>(AchievementChoicePath);
		
		_back.Pressed += GoBack;
		_profileChoice.Pressed += ShowProfileScreen;
		_vaultChoice.Pressed += ShowVaultScreen;
		_appearanceChoice.Pressed += ShowAppearanceScreen;
		_achievementChoice.Pressed += ShowAchievementScreen;
		ApplyThemeAesthetic();
		ShowRequestedScreen();
		
	
	
	}
	
	private void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		// Return to the scene we came from if provided, otherwise go home.
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
	
	
	public void ShowAchievementScreen(){
		AudioManager.Instance?.PlaySelect();
		SetVisibleScreen("achievement");
	}
	public void ShowProfileScreen()
	{
		AudioManager.Instance?.PlaySelect();
		SetVisibleScreen("profile");
	}
	
	public void ShowVaultScreen()
	{
		AudioManager.Instance?.PlaySelect();
		SetVisibleScreen("vault");
	}

	public void ShowAppearanceScreen()
	{
		AudioManager.Instance?.PlaySelect();
		SetVisibleScreen("appearance");
	}

	private void ShowRequestedScreen()
	{
		var tree = GetTree();
		var requested = tree.HasMeta("pgemu_settings_tab")
			? tree.GetMeta("pgemu_settings_tab").AsString()
			: "appearance";

		SetVisibleScreen(string.IsNullOrWhiteSpace(requested) ? "appearance" : requested);
	}

	private void SetVisibleScreen(string screen)
	{
		_profileScreen.Visible = screen == "profile";
		_vaultScreen.Visible = screen == "vault";
		_appearanceScreen.Visible = screen == "appearance";
		_achievementScreen.Visible = screen == "achievement";

		_profileChoice.ButtonPressed = screen == "profile";
		_vaultChoice.ButtonPressed = screen == "vault";
		_appearanceChoice.ButtonPressed = screen == "appearance";
		_achievementChoice.ButtonPressed = screen == "achievement";
	}

	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var sideBg = GetNodeOrNull<ColorRect>("Margin/HBoxContainer/Margin/Bg");
		if (sideBg != null)
			sideBg.Color = new Color(0.115f, 0.093f, 0.182f, 0.94f);

		UiStyle.StyleTopBarButton(_back);
		StyleSectionButton(_profileChoice);
		StyleSectionButton(_vaultChoice);
		StyleSectionButton(_appearanceChoice);
		StyleSectionButton(_achievementChoice);
	}

	private static void StyleSectionButton(Button button)
	{
		button.ToggleMode = true;
		UiStyle.StylePrimaryButton(button);
		button.CustomMinimumSize = new Vector2(180f, 42f);
	}
	

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
