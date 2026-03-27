using Godot;
using System;
using System.Collections.Generic;
using PGEmu.Services;

public partial class Settings : Control
{
	private const float ControllerRowMergeThreshold = 40f;

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
	private string _visibleScreen = "appearance";
	private int _controllerEntryHorizontalAxisDir;
	private long _controllerEntryHorizontalAxisNextMs;
	private int _controllerEntryVerticalAxisDir;
	private long _controllerEntryVerticalAxisNextMs;
	

	
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
	
	public override void _UnhandledInput(InputEvent @event)
	{
		if (ControllerService.TryHandleBackAction(@event, GoBack))
		{
			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (@event is InputEventJoypadButton joypadButton && joypadButton.Pressed)
		{
			if (ControllerService.Instance?.ShouldHandleMenuInput(joypadButton.Device) == false)
				return;
			if (!TryEnterControllerNavigation(joypadButton.ButtonIndex))
				return;

			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (@event is not InputEventJoypadMotion joypadMotion)
			return;
		if (ControllerService.Instance?.ShouldHandleMenuInput(joypadMotion.Device) == false)
			return;
		if (!TryEnterControllerNavigation(joypadMotion))
			return;

		GetViewport()?.SetInputAsHandled();
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
		_visibleScreen = screen;
		_profileScreen.Visible = screen == "profile";
		_vaultScreen.Visible = screen == "vault";
		_appearanceScreen.Visible = screen == "appearance";
		_achievementScreen.Visible = screen == "achievement";

		_profileChoice.ButtonPressed = screen == "profile";
		_vaultChoice.ButtonPressed = screen == "vault";
		_appearanceChoice.ButtonPressed = screen == "appearance";
		_achievementChoice.ButtonPressed = screen == "achievement";

		CallDeferred(nameof(RefreshControllerFocusGraph));
		CallDeferred(nameof(EnsureVisibleControllerFocus));
		ResetControllerEntryAxisState();
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
		ControllerService.PrepareFocusable(button);
		UiStyle.StylePrimaryButton(button);
		button.CustomMinimumSize = new Vector2(180f, 42f);
	}

	private bool TryEnterControllerNavigation(JoyButton button)
	{
		if (HasVisibleFocusOwner())
			return false;

		return button switch
		{
			JoyButton.DpadRight => FocusPanelEntryForActiveSidebar(),
			JoyButton.DpadLeft => FocusActiveSidebarButton(),
			JoyButton.DpadUp => FocusActiveSidebarButton(),
			JoyButton.DpadDown => FocusActiveSidebarButton(),
			JoyButton.A => FocusActiveSidebarButton(),
			_ => false,
		};
	}

	private bool TryEnterControllerNavigation(InputEventJoypadMotion joypadMotion)
	{
		if (HasVisibleFocusOwner())
			return false;

		if (joypadMotion.Axis == JoyAxis.LeftY)
		{
			return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _controllerEntryVerticalAxisDir, ref _controllerEntryVerticalAxisNextMs, _ =>
			{
				FocusActiveSidebarButton();
			});
		}

		if (joypadMotion.Axis != JoyAxis.LeftX)
			return false;

		return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _controllerEntryHorizontalAxisDir, ref _controllerEntryHorizontalAxisNextMs, dir =>
		{
			if (dir < 0)
				FocusActiveSidebarButton();
			else
				FocusPanelEntryForActiveSidebar();
		});
	}

	private void EnsureVisibleControllerFocus()
	{
		if (HasVisibleFocusOwner())
			return;

		FocusActiveSidebarButton();
	}

	private bool HasVisibleFocusOwner()
	{
		return GetViewport()?.GuiGetFocusOwner() is Control focused && focused.IsVisibleInTree();
	}

	private bool FocusActiveSidebarButton()
	{
		var target = GetActiveSidebarButton();
		if (target == null || !target.IsVisibleInTree())
			return false;

		target.GrabFocus();
		return true;
	}

	private Button? GetActiveSidebarButton()
	{
		return _visibleScreen switch
		{
			"profile" => _profileChoice,
			"vault" => _vaultChoice,
			"achievement" => _achievementChoice,
			_ => _appearanceChoice,
		};
	}

	private bool FocusPanelEntryForActiveSidebar()
	{
		var rows = BuildPanelRows(GetActivePanelFocusableControls());
		if (rows.Count == 0)
			return FocusActiveSidebarButton();

		var entries = new List<Control>();
		foreach (var row in rows)
		{
			if (row.Count > 0)
				entries.Add(row[0]);
		}

		if (entries.Count == 0)
			return FocusActiveSidebarButton();

		var source = (Control?)GetActiveSidebarButton() ?? entries[0];
		FindNearestControlByY(entries, GetControlCenterY(source)).GrabFocus();
		return true;
	}

	private void ResetControllerEntryAxisState()
	{
		ControllerService.ResetMenuAxis(ref _controllerEntryHorizontalAxisDir, ref _controllerEntryHorizontalAxisNextMs);
		ControllerService.ResetMenuAxis(ref _controllerEntryVerticalAxisDir, ref _controllerEntryVerticalAxisNextMs);
	}

	private Control GetActiveScreen()
	{
		return _visibleScreen switch
		{
			"profile" => _profileScreen,
			"vault" => _vaultScreen,
			"achievement" => _achievementScreen,
			_ => _appearanceScreen,
		};
	}

	private List<Control> GetSidebarControls()
	{
		var controls = new List<Control>();
		AddFocusableControl(controls, _back);
		AddFocusableControl(controls, _profileChoice);
		AddFocusableControl(controls, _vaultChoice);
		AddFocusableControl(controls, _appearanceChoice);
		AddFocusableControl(controls, _achievementChoice);
		return controls;
	}

	private List<Control> GetActivePanelFocusableControls()
	{
		var controls = new List<Control>();
		AddFocusableDescendants(controls, GetActiveScreen());
		return controls;
	}

	private void AddFocusableDescendants(List<Control> controls, Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is Control control)
				AddFocusableControl(controls, control);

			AddFocusableDescendants(controls, child);
		}
	}

	private void AddFocusableControl(List<Control> controls, Control? control)
	{
		if (control == null || !GodotObject.IsInstanceValid(control))
			return;
		if (!control.IsVisibleInTree())
			return;
		if (!IsControllerFocusable(control))
			return;
		if (control is BaseButton button && button.Disabled)
			return;
		if (controls.Contains(control))
			return;

		ControllerService.PrepareFocusable(control);
		controls.Add(control);
	}

	private static bool IsControllerFocusable(Control control)
	{
		return control is Button ||
			control is LineEdit ||
			control is TextEdit ||
			control is OptionButton;
	}

	private void RefreshControllerFocusGraph()
	{
		var sidebar = GetSidebarControls();
		ResetFocusNeighbors(sidebar);
		ConfigureSidebarNeighbors(sidebar);

		var panelRows = BuildPanelRows(GetActivePanelFocusableControls());
		foreach (var row in panelRows)
		{
			ResetFocusNeighbors(row);
			ConfigurePanelRowNeighbors(row);
		}

		for (int i = 0; i < panelRows.Count - 1; i++)
			ConfigureVerticalNeighbors(panelRows[i], panelRows[i + 1]);

		ConnectSections(sidebar, panelRows);
	}

	private List<List<Control>> BuildPanelRows(List<Control> controls)
	{
		controls.Sort((left, right) =>
		{
			var yCompare = GetControlCenterY(left).CompareTo(GetControlCenterY(right));
			if (yCompare != 0)
				return yCompare;

			return GetControlCenterX(left).CompareTo(GetControlCenterX(right));
		});

		var rows = new List<List<Control>>();
		foreach (var control in controls)
		{
			if (rows.Count == 0)
			{
				rows.Add(new List<Control> { control });
				continue;
			}

			var row = rows[^1];
			if (Mathf.Abs(GetControlCenterY(control) - GetAverageRowCenterY(row)) <= ControllerRowMergeThreshold)
			{
				row.Add(control);
				continue;
			}

			rows.Add(new List<Control> { control });
		}

		foreach (var row in rows)
			row.Sort((left, right) => GetControlCenterX(left).CompareTo(GetControlCenterX(right)));

		return rows;
	}

	private void ResetFocusNeighbors(IReadOnlyList<Control> controls)
	{
		foreach (var control in controls)
		{
			var selfPath = control.GetPathTo(control);
			control.FocusNeighborLeft = selfPath;
			control.FocusNeighborRight = selfPath;
			control.FocusNeighborTop = selfPath;
			control.FocusNeighborBottom = selfPath;
		}
	}

	private void ConfigureSidebarNeighbors(IReadOnlyList<Control> controls)
	{
		for (int i = 0; i < controls.Count; i++)
		{
			var current = controls[i];
			current.FocusNeighborTop = current.GetPathTo(i > 0 ? controls[i - 1] : current);
			current.FocusNeighborBottom = current.GetPathTo(i < controls.Count - 1 ? controls[i + 1] : current);
		}
	}

	private void ConfigurePanelRowNeighbors(IReadOnlyList<Control> row)
	{
		for (int i = 0; i < row.Count; i++)
		{
			var current = row[i];
			current.FocusNeighborLeft = current.GetPathTo(i > 0 ? row[i - 1] : current);
			current.FocusNeighborRight = current.GetPathTo(i < row.Count - 1 ? row[i + 1] : current);
		}
	}

	private void ConfigureVerticalNeighbors(IReadOnlyList<Control> upperRow, IReadOnlyList<Control> lowerRow)
	{
		foreach (var upper in upperRow)
			upper.FocusNeighborBottom = upper.GetPathTo(FindNearestControlByX(lowerRow, GetControlCenterX(upper)));

		foreach (var lower in lowerRow)
			lower.FocusNeighborTop = lower.GetPathTo(FindNearestControlByX(upperRow, GetControlCenterX(lower)));
	}

	private void ConnectSections(IReadOnlyList<Control> sidebar, IReadOnlyList<List<Control>> panelRows)
	{
		if (sidebar.Count == 0 || panelRows.Count == 0)
			return;

		var activeSidebar = (Control?)GetActiveSidebarButton() ?? sidebar[0];
		var panelEntries = new List<Control>();
		foreach (var row in panelRows)
		{
			if (row.Count > 0)
				panelEntries.Add(row[0]);
		}

		if (panelEntries.Count == 0)
			return;

		foreach (var sidebarControl in sidebar)
		{
			sidebarControl.FocusNeighborRight = sidebarControl.GetPathTo(
				FindNearestControlByY(panelEntries, GetControlCenterY(sidebarControl)));
		}

		foreach (var row in panelRows)
		{
			if (row.Count == 0)
				continue;

			var first = row[0];
			first.FocusNeighborLeft = first.GetPathTo(activeSidebar);
		}
	}

	private static Control FindNearestControlByX(IReadOnlyList<Control> row, float sourceCenterX)
	{
		var nearest = row[0];
		var nearestDistance = Mathf.Abs(GetControlCenterX(nearest) - sourceCenterX);

		for (int i = 1; i < row.Count; i++)
		{
			var candidate = row[i];
			var distance = Mathf.Abs(GetControlCenterX(candidate) - sourceCenterX);
			if (distance >= nearestDistance)
				continue;

			nearest = candidate;
			nearestDistance = distance;
		}

		return nearest;
	}

	private static Control FindNearestControlByY(IReadOnlyList<Control> controls, float sourceCenterY)
	{
		var nearest = controls[0];
		var nearestDistance = Mathf.Abs(GetControlCenterY(nearest) - sourceCenterY);

		for (int i = 1; i < controls.Count; i++)
		{
			var candidate = controls[i];
			var distance = Mathf.Abs(GetControlCenterY(candidate) - sourceCenterY);
			if (distance >= nearestDistance)
				continue;

			nearest = candidate;
			nearestDistance = distance;
		}

		return nearest;
	}

	private static float GetAverageRowCenterY(IReadOnlyList<Control> row)
	{
		if (row.Count == 0)
			return 0f;

		float total = 0f;
		foreach (var control in row)
			total += GetControlCenterY(control);

		return total / row.Count;
	}

	private static float GetControlCenterX(Control control)
	{
		var rect = control.GetGlobalRect();
		return rect.Position.X + (rect.Size.X * 0.5f);
	}

	private static float GetControlCenterY(Control control)
	{
		var rect = control.GetGlobalRect();
		return rect.Position.Y + (rect.Size.Y * 0.5f);
	}
	

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
