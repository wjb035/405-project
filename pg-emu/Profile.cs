using Godot;
using PGEmu.app;
using PGEmu.Services;
using PGEmu.Services.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PGEmu.Helpers;
using RetroAchievements.Api;
using FriendRecordStatus = PGEmu.Services.Models.FriendStatus;

public partial class Profile : Control
{
	private sealed class FriendListEntry
	{
		public string Id { get; init; } = string.Empty;
		public string Username { get; init; } = string.Empty;
		public FriendRecordStatus Status { get; init; } = FriendRecordStatus.Accepted;
	}

	private sealed class FriendDrawerRowRefs
	{
		public Button Button { get; init; } = null!;
		public TextureRect Avatar { get; init; } = null!;
		public Label DetailLabel { get; init; } = null!;
	}

	private const int MaxTileCount = 4;
	private const int RecentGamesSeeAllLimit = 10;
	private const float ControllerRowMergeThreshold = 36f;
	private const string SettingsParentReturnSceneMeta = "pgemu_profile_settings_parent_return_scene";
	private const string ReturnSceneMetaKey = "pgemu_return_scene";
	private const string TrophySectionTitlePath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/Showcase";
	private const string ShowcaseTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/TileGrid";
	private const string RecentGamesTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/TileGrid";
	private const string FriendsTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/TileGrid";
	private const string ShowcaseFooterPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/FooterRow";
	private const string RecentGamesFooterPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/FooterRow";
	private const string FriendsFooterPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/FooterRow";
	private const string FriendSearchDropdownPath = "Margin/Root/TopBar/FriendSearchDropdown";
	private const string AvatarCardPath = "Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer2";
	private const string AvatarFrameMarginPath = AvatarCardPath + "/AvatarFrameMargin";
	private const string AvatarFramePath = AvatarFrameMarginPath + "/AvatarFrame";
	private const string AvatarInnerMarginPath = AvatarFramePath + "/AvatarInnerMargin";
	private const string AvatarAspectPath = AvatarInnerMarginPath + "/AspectRatioContainer";
	private const string AvatarStackPath = AvatarAspectPath + "/AvatarStack";
	private const int FriendSearchPlaceholderId = -1;
	private const int FriendSearchUsernamePromptId = -2;
	private const int FriendSearchFriendIdBase = 1000;
	private const int FriendSearchMaxResults = 12;
	private const int FriendSearchHintResults = 3;
	private const float FriendDrawerWidth = 392f;
	private const float FriendDrawerScrimAlpha = 0.54f;
	private const float FriendDrawerTweenSeconds = 0.22f;
	private const string HoverFeedbackAppliedMeta = "pgemu_profile_hover_feedback_applied";
	private const string SceneBackgroundMaterialMeta = "pgemu_profile_scene_background_material_local";
	private const string RetroAchievementsConnectPrompt = "Connect to retroachievements to view your trophies!";
	private const string NoEarnedAchievementsText = "No achievements unlocked yet";
	private const int ShowcaseRecentlyPlayedFetchCount = 50;
	private const int MaxShowcaseLabelLength = 32;

	private sealed class ShowcaseTileEntry
	{
		public int GameId { get; init; }
		public string GameTitle { get; init; } = string.Empty;
		public string Label { get; init; } = string.Empty;
	}

	[Export] public NodePath BackPath;
	[Export] public NodePath AvatarPath;
	[Export] public NodePath ProfileSettingsShortcutPath;

	private readonly ProfileService _profileService = new();
	private readonly System.Net.Http.HttpClient _client = new();
	private readonly Dictionary<Button, string> _friendTileUsernames = new();
	private readonly Dictionary<Button, (int GameId, string GameTitle)> _showcaseGameTargets = new();
	private readonly Dictionary<int, string> _friendSearchEntries = new();
	private readonly Dictionary<string, ProfileResponse> _friendProfileCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Texture2D> _avatarTextureCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<string> _allFriendSearchUsernames = new();
	private readonly List<string> _friendSearchResultUsernames = new();
	private readonly List<FriendListEntry> _friendEntries = new();
	private ProfileVisualStyleCatalog.ResolvedStyle _visualStyle =
		ProfileVisualStyleCatalog.Resolve(null, null, null);

	private Button _back = null!;
	private Button _friendsList = null!;
	private Button? _showcaseSeeAll = null;
	private Button? _recentGamesSeeAll = null;
	private Button _profileSettingsShortcut = null!;
	private ScrollContainer _bodyScroll = null!;
	private Label _gamerTag = null!;
	private Label _profileNote = null!;
	private OptionButton _visibilityToggle = null!;
	private OptionButton? _friendSearchDropdown = null;
	private TextureRect _avatar = null!;
	private ConfirmationDialog _friendSearchDialog = null!;
	private LineEdit _friendSearchInput = null!;
	private ItemList _friendSearchResultsList = null!;
	private Control _friendDrawerOverlay = null!;
	private ColorRect _friendDrawerScrim = null!;
	private PanelContainer _friendDrawerPanel = null!;
	private Label _friendDrawerTitle = null!;
	private Label _friendDrawerCount = null!;
	private Button _friendDrawerClose = null!;
	private ScrollContainer _friendDrawerScroll = null!;
	private VBoxContainer _friendDrawerList = null!;
	private readonly List<Button> _friendDrawerButtons = new();
	private Tween? _friendDrawerTween;
	private bool _friendDrawerOpen;
	private bool _friendDrawerAnimating;
	private int _friendDrawerRequestId;

	private ProfileResponse? _profile;
	private bool _openingFriendProfile;
	private int _uiHorizontalAxisDir;
	private long _uiHorizontalAxisNextMs;
	private int _uiVerticalAxisDir;
	private long _uiVerticalAxisNextMs;
	private int _uiRowIndex = -1;
	private int _uiColumnIndex = -1;
	private int _friendSearchHintRequestId;
	private int _friendSearchOptionsRequestId;
	private int _friendSearchResultsRequestId;
	private bool _openingSectionScene;

	private ScreenTransition Transition =>
		GetNode<ScreenTransition>("/root/ScreenTransition");
	
	public override async void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		_profileSettingsShortcut = GetNode<Button>(ProfileSettingsShortcutPath);
		_bodyScroll = GetNode<ScrollContainer>("Margin/Root/BodyScroll");
		_friendsList = GetNode<Button>("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/FooterRow/Button");
		_gamerTag = GetNode<Label>("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/HeaderRow/GamerTag");
		_profileNote = GetNode<Label>("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer/MarginContainer/ProfileNote");
		_visibilityToggle = GetNode<OptionButton>("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/HeaderRow/OptionButton");
		_friendSearchDropdown = GetNodeOrNull<OptionButton>(FriendSearchDropdownPath);
		_showcaseSeeAll = GetNodeOrNull<Button>(ShowcaseFooterPath + "/Button");
		_recentGamesSeeAll = GetNodeOrNull<Button>(RecentGamesFooterPath + "/Button");
		_avatar = GetNode<TextureRect>(AvatarPath);
		ApplyAvatarMaskShader();

		StartBackgroundTransition();

		ConnectIfNeeded(_back, GoBack);
		ConnectIfNeeded(_profileSettingsShortcut, GoProfileSettings);
		ConnectIfNeeded(_friendsList, GoFriendsList);
		if (_showcaseSeeAll != null)
			ConnectIfNeeded(_showcaseSeeAll, GoAchievements);
		if (_recentGamesSeeAll != null)
			ConnectIfNeeded(_recentGamesSeeAll, GoGameSelect);
		ConnectFriendTileButtons();
		ConnectRecentGameTileButtons();
		ConnectShowcaseTileButtons();
			if (_friendSearchDropdown != null)
				_friendSearchDropdown.ItemSelected += OnFriendSearchSelected;
			SetupFriendSearchDialog();
			SetupFriendDrawer();
			ConfigureFriendSearchDropdown(Array.Empty<string>());

		ApplyThemeAesthetic();
		SetLoadingState();
		await Task.WhenAll(
			LoadProfileAsync(),
			LoadSectionDataAsync());
		CallDeferred(nameof(RefreshControllerFocusGraph));
	}

	private void ApplyAvatarMaskShader()
	{
		var avatarMaterial = _avatar.Material as ShaderMaterial;
		if (avatarMaterial == null)
		{
			avatarMaterial = new ShaderMaterial
			{
				Shader = GD.Load<Shader>(GetAvatarShaderPath())
			};
			_avatar.Material = avatarMaterial;
		}
		else if (avatarMaterial.Shader?.ResourcePath != GetAvatarShaderPath())
		{
			avatarMaterial.Shader = GD.Load<Shader>(GetAvatarShaderPath());
		}

		var style = _visualStyle;
		avatarMaterial.SetShaderParameter("width", style.AvatarFrame.Width);
		if (style.AvatarFrame.Id != "circle")
			avatarMaterial.SetShaderParameter("radius", style.AvatarFrame.Radius);
		avatarMaterial.SetShaderParameter("stroke_width", style.AvatarFrame.StrokeWidth);
		avatarMaterial.SetShaderParameter("stroke_color", style.Background.Accent);
		avatarMaterial.SetShaderParameter("edge_softness", 0.006f);
	}

	private string GetAvatarShaderPath()
	{
		return _visualStyle.AvatarFrame.Id == "circle"
			? "res://ShaderSlop/circle.gdshader"
			: "res://ShaderSlop/RoundedAvatarFrame.gdshader";
	}


		public override void _UnhandledInput(InputEvent @event)
		{
			if (ControllerService.TryHandleBackAction(@event, _friendDrawerOpen || _friendDrawerAnimating ? CloseFriendDrawer : GoBack))
			{
				GetViewport()?.SetInputAsHandled();
				return;
			}

		if (@event is InputEventJoypadMotion joypadMotion)
		{
			if (!ShouldHandleControllerInput(joypadMotion.Device))
				return;

			if (!HandleControllerUiAxis(joypadMotion))
				return;

			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (@event is not InputEventJoypadButton joypadButton || !joypadButton.Pressed)
			return;

		if (!ShouldHandleControllerInput(joypadButton.Device))
			return;

		if (!HandleControllerUiButton(joypadButton.ButtonIndex))
			return;

		GetViewport()?.SetInputAsHandled();
	}

	private bool HandleControllerUiButton(JoyButton button)
	{
		var rows = GetControllerUiRows();
		if (rows.Count == 0)
			return false;

		if (!IsUiNavigationActive())
		{
			switch (button)
			{
				case JoyButton.DpadUp:
					_uiRowIndex = 0;
					_uiColumnIndex = 0;
					return FocusControllerRowEntry(rows);
				case JoyButton.DpadDown:
					_uiRowIndex = rows.Count - 1;
					_uiColumnIndex = 0;
					return FocusControllerRowEntry(rows);
				default:
					return false;
			}
		}

		switch (button)
		{
			case JoyButton.DpadLeft:
				return MoveControllerSelection(rows, 0, -1);
			case JoyButton.DpadRight:
				return MoveControllerSelection(rows, 0, 1);
			case JoyButton.DpadUp:
				return MoveControllerSelection(rows, -1, 0);
			case JoyButton.DpadDown:
				return MoveControllerSelection(rows, 1, 0);
			default:
				return ControllerService.IsConfirmButton(button) &&
					ControllerService.ActivateRowSelection(rows, _uiRowIndex, _uiColumnIndex);
		}
	}

	private bool HandleControllerUiAxis(InputEventJoypadMotion joypadMotion)
	{
		var rows = GetControllerUiRows();
		if (rows.Count == 0)
			return false;

		if (joypadMotion.Axis == JoyAxis.LeftY)
		{
			if (!IsUiNavigationActive())
			{
				return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs, dir =>
				{
					_uiRowIndex = dir < 0 ? 0 : rows.Count - 1;
					_uiColumnIndex = 0;
					FocusControllerRowEntry(rows);
				});
			}

			return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs, dir =>
			{
				MoveControllerSelection(rows, dir, 0);
			});
		}

		if (!IsUiNavigationActive() || joypadMotion.Axis != JoyAxis.LeftX)
			return false;

		return ControllerService.TryHandleMenuAxis(joypadMotion.AxisValue, ref _uiHorizontalAxisDir, ref _uiHorizontalAxisNextMs, dir =>
		{
			MoveControllerSelection(rows, 0, dir);
		});
	}

	private List<List<Button>> GetControllerUiRows()
	{
		if (_friendDrawerOverlay.Visible)
		{
			var rows = new List<List<Button>>
			{
				new() { _friendDrawerClose }
			};

			foreach (var button in _friendDrawerButtons.Where(button => button.Visible && !button.Disabled))
				rows.Add(new List<Button> { button });

			return rows;
		}

		var topRow = new List<Button> { _back };
		if (_friendSearchDropdown != null)
			topRow.Add(_friendSearchDropdown);
		topRow.Add(_profileSettingsShortcut);

		var navigationRows = ControllerService.BuildVisibleRows(topRow, new Button?[] { _visibilityToggle });
		navigationRows.AddRange(BuildSectionTileRows());
		navigationRows.AddRange(ControllerService.BuildVisibleRows(new Button?[] { _showcaseSeeAll, _recentGamesSeeAll, _friendsList }));
		return navigationRows;
	}

	private List<List<Button>> BuildSectionTileRows()
	{
		var showcaseTiles = GetTileButtons(ShowcaseTileGridPath);
		var recentTiles = GetTileButtons(RecentGamesTileGridPath);
		var friendTiles = GetTileButtons(FriendsTileGridPath);

		var maxRows = Mathf.Max(showcaseTiles.Count, Mathf.Max(recentTiles.Count, friendTiles.Count));
		if (maxRows <= 0)
			return new List<List<Button>>();

		var rows = new List<List<Button>>(maxRows);
		for (int index = 0; index < maxRows; index++)
		{
			var row = new List<Button>(3);
			TryAddSectionTile(row, showcaseTiles, index);
			TryAddSectionTile(row, recentTiles, index);
			TryAddSectionTile(row, friendTiles, index);

			if (row.Count > 0)
				rows.Add(row);
		}

		return rows;
	}

	private static void TryAddSectionTile(ICollection<Button> row, IReadOnlyList<Button> sectionTiles, int index)
	{
		if (index < 0 || index >= sectionTiles.Count)
			return;

		var button = sectionTiles[index];
		if (!GodotObject.IsInstanceValid(button) || !button.Visible || button.Disabled)
			return;

		ControllerService.PrepareFocusable(button);
		row.Add(button);
	}

	private bool FocusControllerRowEntry(IReadOnlyList<List<Button>> rows)
	{
		var focused = ControllerService.FocusRowEntry(rows, ref _uiRowIndex, ref _uiColumnIndex);
		if (focused)
			EnsureFocusedControlVisible();
		return focused;
	}

	private bool MoveControllerSelection(IReadOnlyList<List<Button>> rows, int rowDelta, int columnDelta)
	{
		var moved = ControllerService.MoveRowSelection(rows, ref _uiRowIndex, ref _uiColumnIndex, rowDelta, columnDelta);
		if (moved)
			EnsureFocusedControlVisible();
		return moved;
	}

	private bool IsUiNavigationActive()
	{
		return _uiRowIndex >= 0 && _uiColumnIndex >= 0;
	}

	private void ResetUiNavigationState()
	{
		_uiRowIndex = -1;
		_uiColumnIndex = -1;
		ControllerService.ResetMenuAxis(ref _uiHorizontalAxisDir, ref _uiHorizontalAxisNextMs);
		ControllerService.ResetMenuAxis(ref _uiVerticalAxisDir, ref _uiVerticalAxisNextMs);
	}

	private void RefreshControllerFocusGraph()
	{
		var rows = GetControllerUiRows();
		foreach (var row in rows)
			ResetFocusNeighbors(row);

		foreach (var row in rows)
			ConfigureHorizontalNeighbors(row);

		for (int index = 0; index < rows.Count - 1; index++)
			ConfigureVerticalNeighbors(rows[index], rows[index + 1]);
	}

	private void ResetFocusNeighbors(IReadOnlyList<Button> row)
	{
		foreach (var button in row)
		{
			var selfPath = button.GetPathTo(button);
			button.FocusNeighborLeft = selfPath;
			button.FocusNeighborRight = selfPath;
			button.FocusNeighborTop = selfPath;
			button.FocusNeighborBottom = selfPath;
		}
	}

	private void ConfigureHorizontalNeighbors(IReadOnlyList<Button> row)
	{
		if (row.Count == 0)
			return;

		for (int index = 0; index < row.Count; index++)
		{
			var current = row[index];
			var left = row[(index - 1 + row.Count) % row.Count];
			var right = row[(index + 1) % row.Count];
			current.FocusNeighborLeft = current.GetPathTo(left);
			current.FocusNeighborRight = current.GetPathTo(right);
		}
	}

	private void ConfigureVerticalNeighbors(IReadOnlyList<Button> upperRow, IReadOnlyList<Button> lowerRow)
	{
		if (upperRow.Count == 0 || lowerRow.Count == 0)
			return;

		foreach (var upper in upperRow)
			upper.FocusNeighborBottom = upper.GetPathTo(FindNearestButtonByX(lowerRow, GetControlCenterX(upper)));

		foreach (var lower in lowerRow)
			lower.FocusNeighborTop = lower.GetPathTo(FindNearestButtonByX(upperRow, GetControlCenterX(lower)));
	}

	private static Button FindNearestButtonByX(IReadOnlyList<Button> row, float sourceCenterX)
	{
		var nearest = row[0];
		var nearestDistance = Mathf.Abs(GetControlCenterX(nearest) - sourceCenterX);

		for (int index = 1; index < row.Count; index++)
		{
			var candidate = row[index];
			var distance = Mathf.Abs(GetControlCenterX(candidate) - sourceCenterX);
			if (distance >= nearestDistance)
				continue;

			nearest = candidate;
			nearestDistance = distance;
		}

		return nearest;
	}

	private static float GetControlCenterX(Control control)
	{
		var rect = control.GetGlobalRect();
		return rect.Position.X + (rect.Size.X * 0.5f);
	}

		private void EnsureFocusedControlVisible()
		{
			if (GetViewport()?.GuiGetFocusOwner() is not Control focused)
				return;
			if (!GodotObject.IsInstanceValid(focused))
				return;

			if (_friendDrawerOverlay.Visible && _friendDrawerScroll.IsAncestorOf(focused))
			{
				_friendDrawerScroll.EnsureControlVisible(focused);
				return;
			}

			_bodyScroll.EnsureControlVisible(focused);
		}

	private bool ShouldHandleControllerInput(int device)
	{
		return ControllerService.Instance?.ShouldHandleMenuInput(device) ?? true;
	}

	private static void ConnectIfNeeded(Button button, Action handler)
	{
		if (!button.IsConnected(Button.SignalName.Pressed, Callable.From(handler)))
			button.Pressed += handler;
	}

	private void ConnectFriendTileButtons()
	{
		foreach (var button in GetTileButtons(FriendsTileGridPath))
			button.Pressed += () => OpenFriendProfileAsync(button);
	}

	private void ConnectShowcaseTileButtons()
	{
		foreach (var button in GetTileButtons(ShowcaseTileGridPath))
		{
			var captured = button;
			captured.Pressed += () => OpenShowcaseTileTarget(captured);
		}
	}

	private void ConnectRecentGameTileButtons()
	{
		foreach (var button in GetTileButtons(RecentGamesTileGridPath))
		{
			var capturedButton = button;
			capturedButton.Pressed += async () => await OpenGameSelectFromRecentTileAsync(capturedButton);
		}
	}

	private async Task OpenGameSelectFromRecentTileAsync(Button tileButton)
	{
		if (!IsActionableTile(tileButton) || _openingSectionScene)
			return;

		_openingSectionScene = true;
		try
		{
			AudioManager.Instance?.PlaySelect();
			var tree = GetTree();
			CollectionStorage.currentCollection = await LoadRecentGameEntriesAsync();
			CollectionStorage.SearchResult = null;
			tree.SetMeta(ReturnSceneMetaKey, "res://profile.tscn");
			await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Noise, 0.25f, 0.025f, false);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Could not open game select from recent-game tile: {exception.Message}");
		}
		finally
		{
			_openingSectionScene = false;
		}
	}

	private static bool IsActionableTile(Button button)
	{
		if (!GodotObject.IsInstanceValid(button))
			return false;
		if (!button.Visible || button.Disabled)
			return false;

		return !string.IsNullOrWhiteSpace(button.Text);
	}

		private void SetupFriendSearchDialog()
		{
		_friendSearchDialog = new ConfirmationDialog
		{
			Title = "Find Friend",
			DialogText = "Enter a username to view profile:",
			Exclusive = true
		};

		_friendSearchInput = new LineEdit
		{
			PlaceholderText = "Username",
			ClearButtonEnabled = true,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		UiStyle.StyleLineEdit(_friendSearchInput);

		_friendSearchDialog.GetOkButton().Text = "Search";
		_friendSearchDialog.GetCancelButton().Text = "Cancel";
		_friendSearchDialog.GetOkButton().Disabled = true;
		_friendSearchInput.TextChanged += value =>
		{
			_friendSearchDialog.GetOkButton().Disabled = string.IsNullOrWhiteSpace(value);
			_ = UpdateFriendSearchDialogHintAsync(value);
			_ = PopulateFriendSearchOptionsAsync(value);
			_ = UpdateFriendSearchResultsListAsync(value);
		};
		_friendSearchInput.TextSubmitted += text => _ = TriggerFriendSearchFromInputAsync();

		_friendSearchResultsList = new ItemList
		{
			CustomMinimumSize = new Vector2(0, 140),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			FocusMode = Control.FocusModeEnum.None,
			Visible = false
		};
		_friendSearchResultsList.ItemSelected += OnFriendSearchResultSelected;
		_friendSearchResultsList.ItemActivated += OnFriendSearchResultActivated;

		var content = new VBoxContainer();
		content.AddChild(_friendSearchInput);
		content.AddChild(_friendSearchResultsList);
		_friendSearchDialog.AddChild(content);

		_friendSearchDialog.Confirmed += () => _ = TriggerFriendSearchFromInputAsync();

			AddChild(_friendSearchDialog);
		}

		private void SetupFriendDrawer()
		{
			_friendDrawerOverlay = new Control
			{
				Name = "FriendDrawerOverlay",
				Visible = false,
				MouseFilter = MouseFilterEnum.Stop,
				ZIndex = 50
			};
			_friendDrawerOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_friendDrawerOverlay.OffsetLeft = 0f;
			_friendDrawerOverlay.OffsetTop = 0f;
			_friendDrawerOverlay.OffsetRight = 0f;
			_friendDrawerOverlay.OffsetBottom = 0f;
			AddChild(_friendDrawerOverlay);

			_friendDrawerScrim = new ColorRect
			{
				Name = "FriendDrawerScrim",
				Color = new Color(0.03f, 0.02f, 0.06f, 0f),
				MouseFilter = MouseFilterEnum.Stop
			};
			_friendDrawerScrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_friendDrawerScrim.OffsetLeft = 0f;
			_friendDrawerScrim.OffsetTop = 0f;
			_friendDrawerScrim.OffsetRight = 0f;
			_friendDrawerScrim.OffsetBottom = 0f;
			_friendDrawerScrim.GuiInput += OnFriendDrawerScrimInput;
			_friendDrawerOverlay.AddChild(_friendDrawerScrim);

		_friendDrawerPanel = new PanelContainer
		{
			Name = "FriendDrawerPanel",
			MouseFilter = MouseFilterEnum.Stop,
			CustomMinimumSize = new Vector2(FriendDrawerWidth, 0f)
		};
			_friendDrawerPanel.AnchorLeft = 1f;
			_friendDrawerPanel.AnchorRight = 1f;
			_friendDrawerPanel.AnchorTop = 0f;
			_friendDrawerPanel.AnchorBottom = 1f;
			_friendDrawerPanel.OffsetTop = 0f;
			_friendDrawerPanel.OffsetBottom = 0f;
			SetFriendDrawerDockedOffsets(isOpen: false);
			_friendDrawerOverlay.AddChild(_friendDrawerPanel);

			var panelMargin = new MarginContainer();
			panelMargin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			panelMargin.OffsetLeft = 0f;
			panelMargin.OffsetTop = 0f;
			panelMargin.OffsetRight = 0f;
			panelMargin.OffsetBottom = 0f;
			panelMargin.AddThemeConstantOverride("margin_left", 18);
			panelMargin.AddThemeConstantOverride("margin_top", 18);
			panelMargin.AddThemeConstantOverride("margin_right", 18);
			panelMargin.AddThemeConstantOverride("margin_bottom", 18);
			_friendDrawerPanel.AddChild(panelMargin);

			var content = new VBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill
			};
			content.AddThemeConstantOverride("separation", 12);
			panelMargin.AddChild(content);

			var headerRow = new HBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			headerRow.AddThemeConstantOverride("separation", 10);
			content.AddChild(headerRow);

			var titleStack = new VBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			titleStack.AddThemeConstantOverride("separation", 2);
			headerRow.AddChild(titleStack);

			_friendDrawerTitle = new Label
			{
				Name = "FriendDrawerTitle",
				Text = "All Friends"
			};
			titleStack.AddChild(_friendDrawerTitle);

			_friendDrawerCount = new Label
			{
				Name = "FriendDrawerCount",
				Text = "0 total"
			};
			titleStack.AddChild(_friendDrawerCount);

			_friendDrawerClose = new Button
			{
				Name = "FriendDrawerClose",
				Text = "Close",
				CustomMinimumSize = new Vector2(92f, 34f)
			};
			_friendDrawerClose.Pressed += CloseFriendDrawer;
			headerRow.AddChild(_friendDrawerClose);

			content.AddChild(new HSeparator());

			_friendDrawerScroll = new ScrollContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
			};
			content.AddChild(_friendDrawerScroll);

			var listMargin = new MarginContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Ignore
			};
			listMargin.AddThemeConstantOverride("margin_left", 8);
			listMargin.AddThemeConstantOverride("margin_right", 8);
			listMargin.AddThemeConstantOverride("margin_top", 6);
			listMargin.AddThemeConstantOverride("margin_bottom", 6);
			_friendDrawerScroll.AddChild(listMargin);

			_friendDrawerList = new VBoxContainer
			{
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			_friendDrawerList.AddThemeConstantOverride("separation", 10);
			listMargin.AddChild(_friendDrawerList);
		}

		private void OnFriendDrawerScrimInput(InputEvent @event)
		{
			if (@event is not InputEventMouseButton mouseButton || !mouseButton.Pressed)
				return;
			if (mouseButton.ButtonIndex != MouseButton.Left)
				return;

			CloseFriendDrawer();
			AcceptEvent();
		}

	private async Task OpenFriendDrawerAsync()
	{
		if (_friendDrawerOpen || _friendDrawerAnimating)
			return;

		var requestId = ++_friendDrawerRequestId;
		_friendDrawerAnimating = true;
		_friendDrawerOverlay.Visible = true;
		_friendDrawerScrim.Color = new Color(0.03f, 0.02f, 0.06f, 0f);
		SetFriendDrawerDockedOffsets(isOpen: false);

		var friendEntries = _friendEntries.Count > 0
			? _friendEntries.ToArray()
			: await LoadFriendEntriesAsync();

		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
		{
			_friendDrawerAnimating = false;
			return;
		}

		_friendEntries.Clear();
		_friendEntries.AddRange(friendEntries);

		if (_allFriendSearchUsernames.Count == 0)
			ConfigureFriendSearchDropdown(friendEntries.Select(friend => friend.Username).ToArray());

		PopulateFriendDrawer(friendEntries, requestId);
		ApplyFriendDrawerTheme();

		_friendDrawerTween?.Kill();
			_friendDrawerTween = CreateTween();
			_friendDrawerTween.SetParallel(true);
			_friendDrawerTween.SetTrans(Tween.TransitionType.Cubic);
			_friendDrawerTween.SetEase(Tween.EaseType.Out);
			_friendDrawerTween.TweenProperty(
				_friendDrawerScrim,
				"color",
				new Color(0.03f, 0.02f, 0.06f, FriendDrawerScrimAlpha),
				FriendDrawerTweenSeconds);
			_friendDrawerTween.TweenProperty(_friendDrawerPanel, "offset_left", -FriendDrawerWidth, FriendDrawerTweenSeconds);
			_friendDrawerTween.TweenProperty(_friendDrawerPanel, "offset_right", 0f, FriendDrawerTweenSeconds);

			await ToSignal(_friendDrawerTween, Tween.SignalName.Finished);
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			SetFriendDrawerDockedOffsets(isOpen: true);
			_friendDrawerOpen = true;
			_friendDrawerAnimating = false;
			CallDeferred(nameof(RefreshControllerFocusGraph));
			FocusFriendDrawerDefault();
		}

	private void CloseFriendDrawer()
	{
		if (!_friendDrawerOverlay.Visible || _friendDrawerAnimating)
			return;

		_friendDrawerRequestId++;
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(-1);

			_friendDrawerAnimating = true;
			_friendDrawerOpen = false;
			_friendDrawerTween?.Kill();
			_friendDrawerTween = CreateTween();
			_friendDrawerTween.SetParallel(true);
			_friendDrawerTween.SetTrans(Tween.TransitionType.Cubic);
			_friendDrawerTween.SetEase(Tween.EaseType.In);
			_friendDrawerTween.TweenProperty(
				_friendDrawerScrim,
				"color",
				new Color(0.03f, 0.02f, 0.06f, 0f),
				FriendDrawerTweenSeconds);
			_friendDrawerTween.TweenProperty(_friendDrawerPanel, "offset_left", 0f, FriendDrawerTweenSeconds);
			_friendDrawerTween.TweenProperty(_friendDrawerPanel, "offset_right", FriendDrawerWidth, FriendDrawerTweenSeconds);
			_friendDrawerTween.Finished += () =>
			{
				if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
					return;

				_friendDrawerAnimating = false;
				SetFriendDrawerDockedOffsets(isOpen: false);
				_friendDrawerOverlay.Visible = false;
				_friendsList.GrabFocus();
				CallDeferred(nameof(RefreshControllerFocusGraph));
			};
		}

	private void PopulateFriendDrawer(IReadOnlyList<FriendListEntry> friends, int requestId)
	{
		foreach (Node child in _friendDrawerList.GetChildren())
			child.QueueFree();

		_friendDrawerButtons.Clear();
		_friendDrawerCount.Text = friends.Count == 1
			? "1 friend"
			: $"{friends.Count} friends";

		if (friends.Count == 0)
		{
			var emptyState = new Label
			{
				Text = "No friends yet",
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
					CustomMinimumSize = new Vector2(0f, 160f)
				};
			_friendDrawerList.AddChild(emptyState);
			return;
		}

		foreach (var friend in friends)
		{
			if (string.IsNullOrWhiteSpace(friend.Username))
				continue;

			var capturedUsername = friend.Username.Trim();
			var friendButton = new Button
			{
				CustomMinimumSize = new Vector2(0f, 74f),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				Text = string.Empty,
				TooltipText = $"View {capturedUsername}'s profile"
			};

			var margin = new MarginContainer
			{
				MouseFilter = MouseFilterEnum.Ignore
			};
			margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			margin.OffsetLeft = 14f;
			margin.OffsetTop = 10f;
			margin.OffsetRight = -14f;
			margin.OffsetBottom = -10f;
			friendButton.AddChild(margin);

			var row = new HBoxContainer
			{
				MouseFilter = MouseFilterEnum.Ignore,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ExpandFill
			};
			row.AddThemeConstantOverride("separation", 12);
			margin.AddChild(row);

			var avatarFrame = new PanelContainer
			{
				CustomMinimumSize = new Vector2(46f, 46f),
				MouseFilter = MouseFilterEnum.Ignore
			};
			avatarFrame.AddThemeStyleboxOverride(
				"panel",
				CreatePanelStyle(
					new Color(0.15f, 0.12f, 0.22f, 0.96f),
					new Color(0.82f, 0.72f, 0.96f, 0.38f),
					14,
					1));
			row.AddChild(avatarFrame);

			var avatar = new TextureRect
			{
				MouseFilter = MouseFilterEnum.Ignore,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered
			};
			avatar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			avatar.OffsetLeft = 0f;
			avatar.OffsetTop = 0f;
			avatar.OffsetRight = 0f;
			avatar.OffsetBottom = 0f;
			avatarFrame.AddChild(avatar);

			var textStack = new VBoxContainer
			{
				MouseFilter = MouseFilterEnum.Ignore,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
			};
			textStack.AddThemeConstantOverride("separation", 2);
			row.AddChild(textStack);

			var nameLabel = new Label
			{
				MouseFilter = MouseFilterEnum.Ignore,
				Text = capturedUsername,
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			UiStyle.StyleTitleLabel(nameLabel);
			nameLabel.AddThemeFontSizeOverride("font_size", 18);
			nameLabel.AddThemeColorOverride("font_color", new Color(0.98f, 0.96f, 1f, 0.99f));
			textStack.AddChild(nameLabel);

			var detailLabel = new Label
			{
				MouseFilter = MouseFilterEnum.Ignore,
				Text = BuildFriendDetailText(friend, null),
				ClipText = true,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
			};
			UiStyle.StyleMetaLabel(detailLabel);
			detailLabel.AddThemeFontSizeOverride("font_size", 13);
			detailLabel.AddThemeColorOverride("font_color", new Color(0.86f, 0.83f, 0.95f, 0.86f));
			textStack.AddChild(detailLabel);

			var viewLabel = new Label
			{
				MouseFilter = MouseFilterEnum.Ignore,
				Text = "View",
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center
			};
			UiStyle.StyleMetaLabel(viewLabel);
			viewLabel.AddThemeFontSizeOverride("font_size", 12);
			viewLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.74f, 0.86f, 0.86f));
			row.AddChild(viewLabel);

			friendButton.Pressed += async () => await OpenFriendProfileByUsernameAsync(capturedUsername);
			_friendDrawerList.AddChild(friendButton);
			_friendDrawerButtons.Add(friendButton);

			var rowRefs = new FriendDrawerRowRefs
			{
				Button = friendButton,
				Avatar = avatar,
				DetailLabel = detailLabel
			};
			_ = PopulateFriendDrawerRowDetailsAsync(friend, rowRefs, requestId);
		}
	}

	private void FocusFriendDrawerDefault()
	{
		ResetUiNavigationState();
		var rows = GetControllerUiRows();
			if (rows.Count == 0)
				return;

			_uiRowIndex = _friendDrawerButtons.Count > 0 ? 1 : 0;
			_uiColumnIndex = 0;
			if (!FocusControllerRowEntry(rows))
			{
				if (_friendDrawerButtons.Count > 0)
					_friendDrawerButtons[0].GrabFocus();
			else
				_friendDrawerClose.GrabFocus();
		}
	}

	private void SetFriendDrawerDockedOffsets(bool isOpen)
	{
		if (!GodotObject.IsInstanceValid(_friendDrawerPanel))
			return;

		_friendDrawerPanel.OffsetLeft = isOpen ? -FriendDrawerWidth : 0f;
		_friendDrawerPanel.OffsetRight = isOpen ? 0f : FriendDrawerWidth;
	}

	private async Task PopulateFriendDrawerRowDetailsAsync(FriendListEntry friend, FriendDrawerRowRefs rowRefs, int requestId)
	{
		try
		{
			var profile = await GetFriendProfileAsync(friend.Username);
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;
			if (requestId != _friendDrawerRequestId)
				return;
			if (!GodotObject.IsInstanceValid(rowRefs.Button) || !GodotObject.IsInstanceValid(rowRefs.Avatar) || !GodotObject.IsInstanceValid(rowRefs.DetailLabel))
				return;

			rowRefs.DetailLabel.Text = BuildFriendDetailText(friend, profile);
			if (profile == null || string.IsNullOrWhiteSpace(profile.AvatarUrl))
				return;

			var avatarTexture = await LoadDrawerAvatarTextureAsync(profile.AvatarUrl);
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;
			if (requestId != _friendDrawerRequestId)
				return;
			if (!GodotObject.IsInstanceValid(rowRefs.Avatar))
				return;
			if (avatarTexture == null)
				return;

			rowRefs.Avatar.Texture = avatarTexture;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Friend drawer detail load failed for {friend.Username}: {exception.Message}");
		}
	}

	private async Task<ProfileResponse?> GetFriendProfileAsync(string username)
	{
		if (string.IsNullOrWhiteSpace(username))
			return null;

		if (_friendProfileCache.TryGetValue(username, out var cachedProfile))
			return cachedProfile;

		var profile = await _profileService.GetUserProfile(username.Trim());
		if (profile != null && !string.IsNullOrWhiteSpace(profile.Username))
			_friendProfileCache[profile.Username] = profile;

		return profile;
	}

	private async Task<Texture2D?> LoadAvatarTextureAsync(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
			return null;

		var normalizedUrl = NormalizeAvatarUrl(url);
		if (_avatarTextureCache.TryGetValue(normalizedUrl, out var cachedTexture))
			return cachedTexture;

		try
		{
			var avatar = new Image();
			var err = Error.Failed;

			var localAvatarPath = TryResolveLocalAvatarPath(url);
			if (!string.IsNullOrWhiteSpace(localAvatarPath) && File.Exists(localAvatarPath))
			{
				err = avatar.Load(localAvatarPath);
			}
			else
			{
				byte[] imageData = await _client.GetByteArrayAsync(normalizedUrl);
				err = avatar.LoadPngFromBuffer(imageData);
				if (err != Error.Ok)
					err = avatar.LoadJpgFromBuffer(imageData);
				if (err != Error.Ok)
					err = avatar.LoadWebpFromBuffer(imageData);
			}

			if (err != Error.Ok)
			{
				GD.PrintErr("Failed to decode avatar image");
				return null;
			}

			var texture = ImageTexture.CreateFromImage(avatar);
			_avatarTextureCache[normalizedUrl] = texture;
			return texture;
		}
		catch (Exception exception)
		{
			GD.PrintErr("Failed to load image: " + exception.Message);
			return null;
		}
	}

	// Drawer avatars need rounded corners. The shader-on-TextureRect route
	// kept failing in this nesting context, so we bake the same SDF shape
	// (and stroke) produced by RoundedAvatarFrame.gdshader directly into
	// the texture's alpha and color channels.
	private async Task<Texture2D?> LoadDrawerAvatarTextureAsync(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
			return null;

		var strokeColor = WithAlpha(_visualStyle.Background.Accent, 0.85f);
		var normalizedUrl = NormalizeAvatarUrl(url);
		var cacheKey = $"drawer-rounded:{strokeColor.ToHtml()}:{normalizedUrl}";
		if (_avatarTextureCache.TryGetValue(cacheKey, out var cachedTexture))
			return cachedTexture;

		var baseTexture = await LoadAvatarTextureAsync(url);
		if (baseTexture == null)
			return null;

		var sourceImage = baseTexture.GetImage();
		if (sourceImage == null)
			return null;

		try
		{
			var rounded = BuildRoundedAvatarImage(sourceImage, targetSize: 96, strokeColor: strokeColor);
			if (rounded == null)
				return null;

			var texture = ImageTexture.CreateFromImage(rounded);
			_avatarTextureCache[cacheKey] = texture;
			return texture;
		}
		catch (Exception exception)
		{
			GD.PrintErr("Failed to bake rounded avatar: " + exception.Message);
			return null;
		}
	}

	private static Image? BuildRoundedAvatarImage(Image source, int targetSize, Color strokeColor)
	{
		// Uses RoundedAvatarFrame.gdshader's SDF formula (rounded-box with
		// width inset, plus stroke band between width and width-stroke_width).
		// At 46-px display size the shader preset's tiny 0.12 radius is
		// barely visible, so we use a larger radius (~30% of full extent)
		// to match the bolder rounded look the drawer looked best with.
		// EdgeSoftness is bumped above the shader's 0.006 because the CPU
		// bake doesn't get GPU sub-pixel coverage for free; we need a ~1 px
		// transition at bake resolution for smooth AA after downsample.
		const float width = 1.0f;
		const float radius = 0.6f;
		const float strokeWidth = 0.06f;
		const float edgeSoftness = 0.022f;

		var image = (Image)source.Duplicate();
		if (image.GetFormat() != Image.Format.Rgba8)
			image.Convert(Image.Format.Rgba8);

		int srcW = image.GetWidth();
		int srcH = image.GetHeight();
		if (srcW <= 0 || srcH <= 0)
			return null;

		if (srcW != srcH)
		{
			int side = Math.Min(srcW, srcH);
			int xOffset = (srcW - side) / 2;
			int yOffset = (srcH - side) / 2;
			var square = Image.CreateEmpty(side, side, false, Image.Format.Rgba8);
			square.BlitRect(image, new Rect2I(xOffset, yOffset, side, side), Vector2I.Zero);
			image = square;
		}

		image.Resize(targetSize, targetSize, Image.Interpolation.Lanczos);

		float invSize = 1f / targetSize;
		float outerEffectiveRadius = radius * width;
		float outerHalfExtent = width - outerEffectiveRadius;
		float innerWidth = MathF.Max(width - strokeWidth, 0f);
		float innerEffectiveRadius = radius * innerWidth;
		float innerHalfExtent = innerWidth - innerEffectiveRadius;

		for (int y = 0; y < targetSize; y++)
		{
			float uy = ((y + 0.5f) * invSize) * 2f - 1f;
			for (int x = 0; x < targetSize; x++)
			{
				float ux = ((x + 0.5f) * invSize) * 2f - 1f;

				float qxOuter = MathF.Abs(ux) - outerHalfExtent;
				float qyOuter = MathF.Abs(uy) - outerHalfExtent;
				float outsideOuter = MathF.Sqrt(
					MathF.Max(qxOuter, 0f) * MathF.Max(qxOuter, 0f) +
					MathF.Max(qyOuter, 0f) * MathF.Max(qyOuter, 0f));
				float insideOuter = MathF.Min(MathF.Max(qxOuter, qyOuter), 0f);
				float sdfOuter = outsideOuter + insideOuter - outerEffectiveRadius;
				float outerMask = 1f - Smoothstep(0f, edgeSoftness, sdfOuter);

				float qxInner = MathF.Abs(ux) - innerHalfExtent;
				float qyInner = MathF.Abs(uy) - innerHalfExtent;
				float outsideInner = MathF.Sqrt(
					MathF.Max(qxInner, 0f) * MathF.Max(qxInner, 0f) +
					MathF.Max(qyInner, 0f) * MathF.Max(qyInner, 0f));
				float insideInner = MathF.Min(MathF.Max(qxInner, qyInner), 0f);
				float sdfInner = outsideInner + insideInner - innerEffectiveRadius;
				float innerMask = 1f - Smoothstep(0f, edgeSoftness, sdfInner);

				float strokeMask = Mathf.Clamp(outerMask - innerMask, 0f, 1f);
				float strokeWeight = strokeMask * strokeColor.A;

				if (outerMask >= 0.999f && strokeWeight <= 0.001f)
					continue;

				var pixel = image.GetPixel(x, y);
				float clippedA = pixel.A * outerMask;

				float r = Mathf.Lerp(pixel.R, strokeColor.R, strokeWeight);
				float g = Mathf.Lerp(pixel.G, strokeColor.G, strokeWeight);
				float b = Mathf.Lerp(pixel.B, strokeColor.B, strokeWeight);
				float a = MathF.Max(clippedA, strokeWeight);

				image.SetPixel(x, y, new Color(r, g, b, a));
			}
		}

		return image;
	}

	private static float Smoothstep(float edge0, float edge1, float x)
	{
		float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
		return t * t * (3f - 2f * t);
	}

	private static string BuildFriendDetailText(FriendListEntry friend, ProfileResponse? profile)
	{
		if (profile != null && !string.IsNullOrWhiteSpace(profile.Bio))
			return TrimSingleLine(profile.Bio, 84);

		return friend.Status switch
		{
			FriendRecordStatus.Pending => "Pending friend request",
			FriendRecordStatus.Blocked => "Blocked",
			_ => "View friend profile"
		};
	}

	private static string TrimSingleLine(string value, int maxLength)
	{
		var trimmed = value?
			.Replace("\r", " ", StringComparison.Ordinal)
			.Replace("\n", " ", StringComparison.Ordinal)
			.Trim() ?? string.Empty;

		if (trimmed.Length <= maxLength)
			return trimmed;
		if (maxLength <= 3)
			return trimmed[..maxLength];

		return $"{trimmed[..(maxLength - 3)]}...";
	}

	private void ConfigureFriendSearchDropdown(IReadOnlyList<string> usernames)
	{
		if (_friendSearchDropdown == null)
			return;

		_allFriendSearchUsernames.Clear();
		_allFriendSearchUsernames.AddRange(
			usernames
				.Select(name => name?.Trim())
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Cast<string>());

		PopulateFriendSearchOptionsFromCandidates(
			_allFriendSearchUsernames
				.OrderBy(username => username, StringComparer.OrdinalIgnoreCase)
				.Take(FriendSearchMaxResults));
	}

	private void OnFriendSearchSelected(long selectedIndex)
	{
		if (_friendSearchDropdown == null)
			return;

		if (selectedIndex < 0 || selectedIndex >= _friendSearchDropdown.ItemCount)
			return;

		int id = _friendSearchDropdown.GetItemId((int)selectedIndex);
		_friendSearchDropdown.Select(0);

		switch (id)
		{
			case FriendSearchPlaceholderId:
				return;
			case FriendSearchUsernamePromptId:
				_friendSearchInput.Text = string.Empty;
				_friendSearchDialog.GetOkButton().Disabled = true;
				ClearFriendSearchResultsList();
				_ = UpdateFriendSearchDialogHintAsync(string.Empty);
				_friendSearchDialog.PopupCentered(new Vector2I(360, 150));
				_friendSearchInput.GrabFocus();
				return;
			default:
				if (_friendSearchEntries.TryGetValue(id, out var username))
					_ = OpenFriendProfileByUsernameAsync(username);
				return;
		}
	}

	private async Task PopulateFriendSearchOptionsAsync(string? query)
	{
		var search = query?.Trim() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(search))
		{
			PopulateFriendSearchOptionsFromCandidates(
				_allFriendSearchUsernames
					.OrderBy(username => username, StringComparer.OrdinalIgnoreCase)
					.Take(FriendSearchMaxResults));
			return;
		}

		var requestId = ++_friendSearchOptionsRequestId;
		var similarUsers = await _profileService.SearchUsersBySimilarity(search, FriendSearchMaxResults);
		if (requestId != _friendSearchOptionsRequestId || !GodotObject.IsInstanceValid(this) || !IsInsideTree())
			return;

		var usernames = similarUsers
			.Select(user => user.Username?.Trim())
			.Where(username => !string.IsNullOrWhiteSpace(username))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Cast<string>();

		PopulateFriendSearchOptionsFromCandidates(usernames);
	}

	private void PopulateFriendSearchOptionsFromCandidates(IEnumerable<string> candidates)
	{
		if (_friendSearchDropdown == null)
			return;

		_friendSearchEntries.Clear();
		_friendSearchDropdown.Clear();
		_friendSearchDropdown.AddItem("Friend Search", FriendSearchPlaceholderId);
		_friendSearchDropdown.AddItem("Search Username...", FriendSearchUsernamePromptId);

		int index = 0;
		foreach (var username in candidates.Take(FriendSearchMaxResults))
		{
			var trimmed = username?.Trim();
			if (string.IsNullOrWhiteSpace(trimmed))
				continue;

			int id = FriendSearchFriendIdBase + index;
			_friendSearchDropdown.AddItem(trimmed, id);
			_friendSearchEntries[id] = trimmed;
			index++;
		}

		_friendSearchDropdown.Select(0);
	}

	private async Task UpdateFriendSearchDialogHintAsync(string? query)
	{
		var search = query?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(search))
		{
			_friendSearchDialog.DialogText = "Enter a username to view profile:";
			return;
		}

		var requestId = ++_friendSearchHintRequestId;
		var similarUsers = await _profileService.SearchUsersBySimilarity(search, FriendSearchHintResults);
		if (requestId != _friendSearchHintRequestId || !GodotObject.IsInstanceValid(this) || !IsInsideTree())
			return;

		var similar = similarUsers
			.Select(user => user.Username?.Trim())
			.Where(username => !string.IsNullOrWhiteSpace(username))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Cast<string>()
			.ToArray();

		_friendSearchDialog.DialogText = similar.Length == 0
			? "No similar usernames found. Try a broader search."
			: $"Similar: {string.Join(", ", similar)}";
	}

	private async Task UpdateFriendSearchResultsListAsync(string? query)
	{
		var search = query?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(search))
		{
			ClearFriendSearchResultsList();
			return;
		}

		var requestId = ++_friendSearchResultsRequestId;
		var similarUsers = await _profileService.SearchUsersBySimilarity(search, FriendSearchMaxResults);
		if (requestId != _friendSearchResultsRequestId || !GodotObject.IsInstanceValid(this) || !IsInsideTree())
			return;

		var usernames = similarUsers
			.Select(user => user.Username?.Trim())
			.Where(username => !string.IsNullOrWhiteSpace(username))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Cast<string>();

		PopulateFriendSearchResultsList(usernames);
		CallDeferred(nameof(RestoreFriendSearchInputFocus));
	}

	private async Task TriggerFriendSearchFromInputAsync()
	{
		var username = ResolveFriendSearchUsername();
		await PopulateFriendSearchOptionsAsync(username);
		var opened = await OpenFriendProfileByUsernameAsync(username);
		if (!opened)
			await UpdateFriendSearchDialogHintAsync(username);
	}

	private void PopulateFriendSearchResultsList(IEnumerable<string> usernames)
	{
		_friendSearchResultUsernames.Clear();
		_friendSearchResultsList.Clear();

		foreach (var username in usernames.Take(FriendSearchMaxResults))
		{
			var trimmed = username?.Trim();
			if (string.IsNullOrWhiteSpace(trimmed))
				continue;

			_friendSearchResultUsernames.Add(trimmed);
			_friendSearchResultsList.AddItem(trimmed);
		}

		_friendSearchResultsList.Visible = _friendSearchResultUsernames.Count > 0;
	}

	private void ClearFriendSearchResultsList()
	{
		_friendSearchResultUsernames.Clear();
		_friendSearchResultsList.Clear();
		_friendSearchResultsList.Visible = false;
	}

	private void OnFriendSearchResultSelected(long index)
	{
		if (index < 0 || index >= _friendSearchResultUsernames.Count)
			return;

		var username = _friendSearchResultUsernames[(int)index];
		_friendSearchInput.Text = username;
		_friendSearchInput.CaretColumn = username.Length;
		_friendSearchDialog.GetOkButton().Disabled = false;
		CallDeferred(nameof(RestoreFriendSearchInputFocus));
	}

	private void RestoreFriendSearchInputFocus()
	{
		if (!GodotObject.IsInstanceValid(_friendSearchInput) || !_friendSearchInput.IsInsideTree())
			return;

		_friendSearchInput.GrabFocus();
		_friendSearchInput.CaretColumn = (_friendSearchInput.Text ?? string.Empty).Length;
	}

	private async void OnFriendSearchResultActivated(long index)
	{
		if (index < 0 || index >= _friendSearchResultUsernames.Count)
			return;

		var username = _friendSearchResultUsernames[(int)index];
		var opened = await OpenFriendProfileByUsernameAsync(username);
		if (opened && GodotObject.IsInstanceValid(_friendSearchDialog))
			_friendSearchDialog.Hide();
	}

	private string ResolveFriendSearchUsername()
	{
		if (GodotObject.IsInstanceValid(_friendSearchResultsList))
		{
			var selected = _friendSearchResultsList.GetSelectedItems();
			if (selected.Length > 0)
			{
				var selectedIndex = selected[0];
				if (selectedIndex >= 0 && selectedIndex < _friendSearchResultUsernames.Count)
					return _friendSearchResultUsernames[selectedIndex];
			}
		}

		return _friendSearchInput.Text?.Trim() ?? string.Empty;
	}

	private async Task LoadProfileAsync()
	{
		try
		{
			_profile = await _profileService.GetMyProfile();
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			if (_profile == null)
			{
				SetFailedState();
				return;
			}

			ApplyProfileVisualStyle(_profile);

			_gamerTag.Text = string.IsNullOrWhiteSpace(_profile.Username) ? "Player" : _profile.Username;
			_profileNote.Text = string.IsNullOrWhiteSpace(_profile.Bio)
				? "No bio yet."
				: _profile.Bio;

			if (!string.IsNullOrWhiteSpace(_profile.AvatarUrl))
				_ = LoadAvatar(_profile.AvatarUrl);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Profile load failed: {exception.Message}");
			if (GodotObject.IsInstanceValid(this) && IsInsideTree())
				SetFailedState();
		}
	}

	private void SetLoadingState()
	{
		_gamerTag.Text = "Profile";
		_profileNote.Text = "Loading profile...";
	}

	private void SetFailedState()
	{
		_profileNote.Text = "Unable to load profile right now.";
	}

	private void ApplyProfileVisualStyle(ProfileResponse profile)
	{
		_visualStyle = ProfileVisualStyleCatalog.Resolve(
			profile.ProfileAccent,
			profile.AvatarFrame,
			profile.ProfileBackground);
		ApplyAvatarMaskShader();
		ApplyThemeAesthetic();
		StartBackgroundTransition(0.85f);
	}

	private async Task LoadSectionDataAsync()
	{
		try
		{
			var trophyTask = LoadShowcaseTileDataAsync();
			var recentGamesTask = LoadRecentGameLabelsAsync();
			var friendsTask = LoadFriendEntriesAsync();

			await Task.WhenAll(trophyTask, recentGamesTask, friendsTask);
			var trophyData = await trophyTask;
			var recentGameItems = await recentGamesTask;
			var friendItems = await friendsTask;
			var previewFriendItems = friendItems
				.Select(friend => friend.Username)
				.Take(MaxTileCount)
				.ToArray();

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			_friendEntries.Clear();
			_friendEntries.AddRange(friendItems);

			ApplyShowcaseTileContent(trophyData.Entries, trophyData.EmptyText);
			ApplyTileContent(RecentGamesTileGridPath, recentGameItems, "No games found");
			ApplyFriendTileContent(previewFriendItems, "No friends yet");
			ConfigureFriendSearchDropdown(friendItems.Select(friend => friend.Username).ToArray());

			SetFooterVisible(ShowcaseFooterPath, true);
			SetFooterVisible(RecentGamesFooterPath, true);
			SetFooterVisible(FriendsFooterPath, true);
			CallDeferred(nameof(RefreshControllerFocusGraph));
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Profile sections failed to load: {exception.Message}");

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			_friendEntries.Clear();
			ApplyShowcaseTileContent(Array.Empty<ShowcaseTileEntry>(), "Unable to load achievements right now");
			ApplyTileContent(RecentGamesTileGridPath, Array.Empty<string>(), "No games found");
			ApplyFriendTileContent(Array.Empty<string>(), "No friends yet");
			ConfigureFriendSearchDropdown(Array.Empty<string>());
			SetFooterVisible(ShowcaseFooterPath, true);
			SetFooterVisible(RecentGamesFooterPath, true);
			SetFooterVisible(FriendsFooterPath, true);
			CallDeferred(nameof(RefreshControllerFocusGraph));
		}
	}

	private void ApplyTileContent(string containerPath, IReadOnlyList<string> items, string emptyText)
	{
		var buttons = GetTileButtons(containerPath);
		if (buttons.Count == 0)
			return;

		if (items.Count == 0)
		{
			for (int index = 0; index < buttons.Count; index++)
			{
				var button = buttons[index];
				button.Visible = index == 0;
				button.Disabled = index == 0;
				button.Text = index == 0 ? emptyText : string.Empty;
				button.TooltipText = index == 0 ? emptyText : string.Empty;
			}

			return;
		}

		for (int index = 0; index < buttons.Count; index++)
		{
			var button = buttons[index];
			if (index < items.Count)
			{
				var text = items[index];
				button.Visible = true;
				button.Disabled = false;
				button.Text = text;
				button.TooltipText = text;
			}
			else
			{
				button.Visible = false;
				button.Disabled = false;
				button.Text = string.Empty;
				button.TooltipText = string.Empty;
			}
		}
	}

	private void ApplyFriendTileContent(IReadOnlyList<string> usernames, string emptyText)
	{
		var buttons = GetTileButtons(FriendsTileGridPath);
		if (buttons.Count == 0)
			return;

		_friendTileUsernames.Clear();

		if (usernames.Count == 0)
		{
			for (int index = 0; index < buttons.Count; index++)
			{
				var button = buttons[index];
				button.Visible = index == 0;
				button.Disabled = index == 0;
				button.Text = index == 0 ? emptyText : string.Empty;
				button.TooltipText = index == 0 ? emptyText : string.Empty;
				ClearFriendTileTarget(button);
			}

			return;
		}

		for (int index = 0; index < buttons.Count; index++)
		{
			var button = buttons[index];
			if (index < usernames.Count)
			{
				var username = usernames[index].Trim();
				button.Visible = true;
				button.Disabled = string.IsNullOrWhiteSpace(username);
				button.Text = username;
				button.TooltipText = string.IsNullOrWhiteSpace(username)
					? string.Empty
					: $"View {username}'s profile";

				if (button.Disabled)
				{
					ClearFriendTileTarget(button);
				}
				else
				{
					_friendTileUsernames[button] = username;
					button.SetMeta("pgemu_friend_username", username);
				}
			}
			else
			{
				button.Visible = false;
				button.Disabled = true;
				button.Text = string.Empty;
				button.TooltipText = string.Empty;
				ClearFriendTileTarget(button);
			}
		}
	}

	private void ApplyShowcaseTileContent(IReadOnlyList<ShowcaseTileEntry> entries, string emptyText)
	{
		var buttons = GetTileButtons(ShowcaseTileGridPath);
		if (buttons.Count == 0)
			return;

		_showcaseGameTargets.Clear();

		if (entries.Count == 0)
		{
			for (int index = 0; index < buttons.Count; index++)
			{
				var button = buttons[index];
				button.Visible = index == 0;
				button.Disabled = index == 0;
				button.Text = index == 0 ? emptyText : string.Empty;
				button.TooltipText = index == 0 ? emptyText : string.Empty;
				button.ClipText = true;
			}

			return;
		}

		for (int index = 0; index < buttons.Count; index++)
		{
			var button = buttons[index];
			button.ClipText = true;

			if (index < entries.Count)
			{
				var entry = entries[index];
				button.Visible = true;
				button.Disabled = entry.GameId <= 0;
				button.Text = entry.Label;
				button.TooltipText = string.IsNullOrWhiteSpace(entry.GameTitle)
					? entry.Label
					: $"View achievements for {entry.GameTitle}";

				if (entry.GameId > 0)
					_showcaseGameTargets[button] = (entry.GameId, entry.GameTitle);
			}
			else
			{
				button.Visible = false;
				button.Disabled = false;
				button.Text = string.Empty;
				button.TooltipText = string.Empty;
			}
		}
	}

	private void OpenShowcaseTileTarget(Button button)
	{
		if (!_showcaseGameTargets.TryGetValue(button, out var target) || target.GameId <= 0)
			return;

		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(1);
		AchievementStorage.gameId = target.GameId;
		AchievementStorage.gameName = target.GameTitle ?? string.Empty;
		AchievementStorage.achievementData = null;
		var tree = GetTree();
		tree.SetMeta(ReturnSceneMetaKey, "res://profile.tscn");
		tree.ChangeSceneToFile("res://Achievements.tscn");
	}

	private void SetFooterVisible(string footerPath, bool visible)
	{
		if (GetNodeOrNull<Control>(footerPath) is Control footer)
			footer.Visible = visible;
	}

	private List<Button> GetTileButtons(string containerPath)
	{
		var container = GetNodeOrNull<Node>(containerPath);
		if (container == null)
			return new List<Button>();

		return container.GetChildren().OfType<Button>().ToList();
	}

	private static void ClearFriendTileTarget(Button button)
	{
		if (button.HasMeta("pgemu_friend_username"))
			button.RemoveMeta("pgemu_friend_username");
	}

	private async Task<(IReadOnlyList<ShowcaseTileEntry> Entries, string EmptyText)> LoadShowcaseTileDataAsync()
	{
		if (!HasRetroAchievementsCredentials())
			return (Array.Empty<ShowcaseTileEntry>(), RetroAchievementsConnectPrompt);

		try
		{
			var response = await RetroAchievementsService.client
				.GetUserRecentlyPlayedGamesAsync(RetroAchievementsService.username, 0, ShowcaseRecentlyPlayedFetchCount);

			var items = response?.Items;
			if (items == null)
				return (Array.Empty<ShowcaseTileEntry>(), NoEarnedAchievementsText);

			var entries = items
				.Where(game => game != null
					&& game.GameId > 0
					&& game.PossibleAchievementsCount > 0
					&& game.EarnedAchievements > 0)
				.GroupBy(game => game.GameId)
				.Select(group => group.First())
				.OrderByDescending(game => game.EarnedAchievements)
				.ThenByDescending(game => game.LastPlayedDate)
				.Take(MaxTileCount)
				.Select(game => new ShowcaseTileEntry
				{
					GameId = game.GameId,
					GameTitle = game.Title?.Trim() ?? string.Empty,
					Label = FormatShowcaseLabel(game.Title, game.EarnedAchievements, game.PossibleAchievementsCount)
				})
				.ToArray();

			return (entries, NoEarnedAchievementsText);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Showcase achievement load failed: {exception.Message}");
			return (Array.Empty<ShowcaseTileEntry>(), "Unable to load achievements right now");
		}
	}

	private static bool HasRetroAchievementsCredentials()
	{
		return !string.IsNullOrWhiteSpace(RetroAchievementsService.username) &&
			!string.IsNullOrWhiteSpace(RetroAchievementsService.apiKey);
	}

	private static string FormatShowcaseLabel(string? gameTitle, int earned, int possible)
	{
		var trimmedTitle = string.IsNullOrWhiteSpace(gameTitle) ? "Unknown Game" : gameTitle.Trim();
		var counter = $"{earned}/{possible}";
		var nameBudget = Math.Max(6, MaxShowcaseLabelLength - counter.Length - 4);
		var displayTitle = trimmedTitle.Length > nameBudget
			? trimmedTitle[..(nameBudget - 1)].TrimEnd() + "\u2026"
			: trimmedTitle;
		return $"{displayTitle}  |  {counter}";
	}

	private async Task<IReadOnlyList<string>> LoadRecentGameLabelsAsync()
	{
		try
		{
			var recentGames = await LoadRecentGameEntriesAsync(MaxTileCount);
			var labels = recentGames
				.Select(FormatRecentGameLabel)
				.ToArray();

			if (labels.Length > 0)
				return labels;

			var playtimeLookup = await LoadPlaytimeLookupAsync();

			return playtimeLookup.Values
				.GroupBy(GetGameIdentity, StringComparer.OrdinalIgnoreCase)
				.Select(group => group
					.OrderByDescending(game => game.TimePlayed)
					.First())
				.OrderByDescending(game => game.TimePlayed)
				.ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
				.Take(MaxTileCount)
				.Select(FormatRecentGameLabel)
				.ToArray();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Recent games load failed: {exception.Message}");
			return Array.Empty<string>();
		}
	}

	private async Task<List<GameEntry>> LoadRecentGameEntriesAsync(int? limit = null)
	{
		var installedGames = LoadInstalledGames()
			.Where(game => game != null && !string.IsNullOrWhiteSpace(game.Path) && game.platform != null)
			.ToList();
		var playtimeLookup = await LoadPlaytimeLookupAsync();

		foreach (var game in installedGames)
		{
			game.TimePlayed = ResolveTrackedPlaytime(playtimeLookup, game);
			game.LastPlayedUnixTime = ResolveTrackedLastPlayed(playtimeLookup, game);
		}

		IEnumerable<GameEntry> recentGames = installedGames
			.GroupBy(GetGameIdentity, StringComparer.OrdinalIgnoreCase)
			.Select(group => group
				.OrderByDescending(game => game.LastPlayedUnixTime)
				.ThenByDescending(game => game.TimePlayed)
				.ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
				.First())
			.OrderByDescending(game => game.LastPlayedUnixTime > 0)
			.ThenByDescending(game => game.LastPlayedUnixTime)
			.ThenByDescending(game => game.TimePlayed > 0)
			.ThenByDescending(game => game.TimePlayed)
			.ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase);

		if (limit.HasValue)
			recentGames = recentGames.Take(limit.Value);

		return recentGames.ToList();
	}

	private async Task<IReadOnlyList<FriendListEntry>> LoadFriendEntriesAsync()
	{
		try
		{
			var friendsJson = await FriendActivity.GetFriendsJson();
			if (!string.IsNullOrWhiteSpace(friendsJson))
			{
				var friendsRoot = JsonSerializer.Deserialize<JsonElement>(friendsJson);
				var parsed = ParseFriendEntries(friendsRoot);
				if (parsed.Count > 0)
					return parsed;
			}

			var response = await AuthService.Instance.SendAuthorizedRequest("http://localhost:5276/api/friends");
			if (response == null)
				return Array.Empty<FriendListEntry>();

			return ParseFriendEntries(response.Value);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Friend list load failed: {exception.Message}");
			return Array.Empty<FriendListEntry>();
		}
	}

	private static IReadOnlyList<FriendListEntry> ParseFriendEntries(JsonElement friendsRoot)
	{
		if (friendsRoot.ValueKind == JsonValueKind.Object &&
			TryGetPropertyIgnoreCase(friendsRoot, "friends", out var nestedFriends))
		{
			friendsRoot = nestedFriends;
		}

		if (friendsRoot.ValueKind != JsonValueKind.Array)
			return Array.Empty<FriendListEntry>();

		return friendsRoot.EnumerateArray()
			.Where(friend => friend.ValueKind == JsonValueKind.Object)
			.Select(friend => new FriendListEntry
			{
				Id = ReadJsonString(friend, "id"),
				Username = ReadJsonString(friend, "username").Trim(),
				Status = FriendRecordStatus.Accepted
			})
			.Where(friend => !string.IsNullOrWhiteSpace(friend.Username))
			.GroupBy(friend => friend.Username, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First())
			.OrderBy(friend => friend.Username, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private async Task<IReadOnlyList<string>> LoadFriendNamesAsync(int? limit = MaxTileCount)
	{
		var friends = await LoadFriendEntriesAsync();
		var usernames = friends.Select(friend => friend.Username);

		if (limit.HasValue)
			usernames = usernames.Take(limit.Value);

		return usernames.ToArray();
	}

	private List<GameEntry> LoadInstalledGames()
	{
		var configPath = ResolveConfigPath();
		if (string.IsNullOrWhiteSpace(configPath))
			return new List<GameEntry>();

		try
		{
			var config = AppConfig.Load(configPath);
			config.LibraryRoot = ExpandHomePath(config.LibraryRoot);

			var installedGames = new List<GameEntry>();
			foreach (var platform in config.Platforms ?? new List<PlatformConfig>())
			{
				if (platform == null)
					continue;

				try
				{
					var scanned = LibraryScanner.Scan(platform, config.LibraryRoot, out _);
					foreach (var game in scanned)
					{
						game.platform = platform;
						installedGames.Add(game);
					}
				}
				catch (Exception exception)
				{
					GD.PrintErr($"Game scan failed for {platform.Name}: {exception.Message}");
				}
			}

			return installedGames;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Config load failed for profile games: {exception.Message}");
			return new List<GameEntry>();
		}
	}

	private async Task<Dictionary<string, GameEntry>> LoadPlaytimeLookupAsync()
	{
		try
		{
			var playtimePath = ProjectSettings.GlobalizePath("res://playtime.json");
			if (!File.Exists(playtimePath))
				return new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);

			// Entries written before LastPlayedUnixTime existed will deserialize with the
			// default value of 0, which makes them sort below any game that has been played
			// since the field was added. Fall back to the playtime.json's last-write time
			// so legacy played games at least sort above never-played ones; once the user
			// launches any of them, FindPlatform will overwrite this with a real timestamp.
			long fallbackLastPlayedUnixTime = 0;
			try
			{
				fallbackLastPlayedUnixTime = new DateTimeOffset(File.GetLastWriteTimeUtc(playtimePath))
					.ToUnixTimeSeconds();
			}
			catch
			{
				// If we can't read the mtime, just leave legacy entries at 0.
			}

			await using var stream = File.OpenRead(playtimePath);
			var playtimeGroups = await JsonSerializer.DeserializeAsync<List<KeyValuePair<string, List<GameEntry>>>>(
				stream,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

			var lookup = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
			if (playtimeGroups == null)
				return lookup;

			foreach (var group in playtimeGroups)
			{
				if (group.Value == null)
					continue;

				foreach (var game in group.Value)
				{
					if (game == null || string.IsNullOrWhiteSpace(game.Name))
						continue;

					if (game.TimePlayed > 0 && game.LastPlayedUnixTime == 0 && fallbackLastPlayedUnixTime > 0)
						game.LastPlayedUnixTime = fallbackLastPlayedUnixTime;

					UpdateLookupEntry(lookup, GetGameIdentity(game), game);
					UpdateLookupEntry(lookup, NormalizeGameName(game.Name), game);
				}
			}

			return lookup;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Playtime load failed: {exception.Message}");
			return new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private static int ResolveTrackedPlaytime(IReadOnlyDictionary<string, GameEntry> playtimeLookup, GameEntry game)
	{
		if (playtimeLookup.TryGetValue(GetGameIdentity(game), out var exactMatch))
			return exactMatch.TimePlayed;

		var normalizedName = NormalizeGameName(game.Name);
		if (playtimeLookup.TryGetValue(normalizedName, out var nameMatch))
			return nameMatch.TimePlayed;

		return 0;
	}

	private static long ResolveTrackedLastPlayed(IReadOnlyDictionary<string, GameEntry> playtimeLookup, GameEntry game)
	{
		if (playtimeLookup.TryGetValue(GetGameIdentity(game), out var exactMatch))
			return exactMatch.LastPlayedUnixTime;

		var normalizedName = NormalizeGameName(game.Name);
		if (playtimeLookup.TryGetValue(normalizedName, out var nameMatch))
			return nameMatch.LastPlayedUnixTime;

		return 0;
	}

	private static string FormatRecentGameLabel(GameEntry game)
	{
		var name = game.Name?.Trim();
		if (string.IsNullOrWhiteSpace(name))
			name = "Unknown Game";

		return game.TimePlayed > 0
			? $"{name}  |  {FormatPlaytime(game.TimePlayed)}"
			: name;
	}

	private static string FormatPlaytime(int totalSeconds)
	{
		if (totalSeconds < 60)
			return $"{totalSeconds}s";

		var minutes = totalSeconds / 60;
		if (minutes < 60)
			return $"{minutes}m";

		var hours = minutes / 60;
		var remainingMinutes = minutes % 60;
		return remainingMinutes == 0 ? $"{hours}h" : $"{hours}h {remainingMinutes}m";
	}

	private static string GetGameIdentity(GameEntry game)
	{
		if (!string.IsNullOrWhiteSpace(game.Path))
			return game.Path.Replace('\\', '/').Trim().ToLowerInvariant();

		return NormalizeGameName(game.Name);
	}

	private static void UpdateLookupEntry(IDictionary<string, GameEntry> lookup, string key, GameEntry game)
	{
		if (string.IsNullOrWhiteSpace(key))
			return;

		if (!lookup.TryGetValue(key, out var existing))
		{
			lookup[key] = game;
			return;
		}

		// When the same name/path appears under multiple emulator groups, prefer the
		// entry that was played most recently so recency lookups stay correct. Only fall
		// back to total playtime when neither side has a known LastPlayedUnixTime, or
		// when both share the exact same timestamp.
		if (game.LastPlayedUnixTime > existing.LastPlayedUnixTime ||
			(game.LastPlayedUnixTime == existing.LastPlayedUnixTime && game.TimePlayed > existing.TimePlayed))
		{
			lookup[key] = game;
		}
	}

	private static string NormalizeGameName(string? name)
	{
		return string.IsNullOrWhiteSpace(name)
			? string.Empty
			: name.Trim().ToLowerInvariant();
	}

	private static string ReadJsonString(JsonElement element, string propertyName)
	{
		if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
			return string.Empty;

		return property.ValueKind switch
		{
			JsonValueKind.String => property.GetString() ?? string.Empty,
			JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.GetRawText(),
			_ => string.Empty,
		};
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement property)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (var candidate in element.EnumerateObject())
			{
				if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
				{
					property = candidate.Value;
					return true;
				}
			}
		}

		property = default;
		return false;
	}

	private static string? ResolveConfigPath()
	{
		return ConfigFinder.FindConfigPath() ?? TryFindConfigNearGodotProject();
	}

	private static string? TryFindConfigNearGodotProject()
	{
		try
		{
			var projectDir = ProjectSettings.GlobalizePath("res://");
			var inProject = Path.Combine(projectDir, "config.json");
			if (File.Exists(inProject))
				return inProject;

			var inParent = Path.GetFullPath(Path.Combine(projectDir, "..", "config.json"));
			if (File.Exists(inParent))
				return inParent;
		}
		catch
		{
			// Best-effort lookup for editor/runtime differences.
		}

		return null;
	}

	private static string ExpandHomePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return path;

		if (path == "~")
			return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

		if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
		{
			var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
			var rest = path.Substring(2);
			return Path.Combine(home, rest);
		}

		return path;
	}

	private void ApplyThemeAesthetic()
	{
		ApplySceneBackgroundShader();

		var theme = _visualStyle.Background;
		var accentPrimary = theme.Accent;
		var cardSurface = _visualStyle.Background.CardSurface;
		var cardSurfaceAlt = _visualStyle.Background.CardSurfaceAlt;
		var cardSurfaceInset = _visualStyle.Background.CardSurfaceInset;
		var cardBorder = WithAlpha(Mix(accentPrimary, new Color(0.56f, 0.48f, 0.76f, 1f), 0.50f), 0.46f);
		var cardBorderStrong = WithAlpha(Mix(accentPrimary, new Color(0.97f, 0.95f, 1f, 1f), 0.22f), 0.62f);
		var avatarBorder = WithAlpha(accentPrimary, 0.62f);
		var chipSurface = _visualStyle.Background.ChipSurface;
		var chipAccent = WithAlpha(accentPrimary, 0.84f);
		var showcaseAccent = WithAlpha(theme.ShowcaseAccent, 0.92f);
		var recentAccent = WithAlpha(theme.RecentAccent, 0.92f);
		var friendsAccent = WithAlpha(theme.FriendsAccent, 0.92f);
		var separatorColor = _visualStyle.Background.Separator;
		var themeTileAccents = theme.TileAccents;

		UiStyle.StyleTopBarButton(_back);
		ApplyProfileHoverFeedback(_back, scaleUp: 1.08f);
		UiStyle.TightenButtonContentPadding(_back, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_back, chipSurface, showcaseAccent, isChip: true);
		UiStyle.StylePopupMenu(_visibilityToggle.GetPopup());
		if (_friendSearchDropdown != null)
			UiStyle.StylePopupMenu(_friendSearchDropdown.GetPopup());
		UiStyle.StyleTopBarButton(_profileSettingsShortcut);
		ApplyProfileHoverFeedback(_profileSettingsShortcut, scaleUp: 1.08f);
		UiStyle.TightenButtonContentPadding(_profileSettingsShortcut, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_profileSettingsShortcut, chipSurface, showcaseAccent, isChip: true);

		UiStyle.StyleOptionButton(_visibilityToggle);
		ApplyProfileHoverFeedback(_visibilityToggle, scaleUp: 1.08f);
		UiStyle.TightenButtonContentPadding(_visibilityToggle, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_visibilityToggle, chipSurface, chipAccent, isChip: true);

		if (_friendSearchDropdown != null)
		{
			UiStyle.StyleOptionButton(_friendSearchDropdown);
			ApplyProfileHoverFeedback(_friendSearchDropdown, scaleUp: 1.08f);
			UiStyle.TightenButtonContentPadding(_friendSearchDropdown, horizontal: 8f, vertical: 3f);
			ApplyButtonTheme(_friendSearchDropdown, chipSurface, friendsAccent, isChip: true);
		}

		if (GetNodeOrNull<Label>("Margin/Root/TopBar/Title") is Label title)
		{
			UiStyle.StyleTitleLabel(title);
			title.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 1f, 0.98f));
		}
		UiStyle.StyleTitleLabel(_gamerTag);
		UiStyle.StyleStatusLabel(_profileNote);

		StyleSectionTitle(TrophySectionTitlePath, showcaseAccent);
		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/Label", recentAccent);
		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Label", friendsAccent);

		ApplyCardStyle(AvatarCardPath, cardSurfaceAlt, avatarBorder, _visualStyle.AvatarFrame.PanelRadius, 1);
		ApplyAvatarFrameLayout();
		ApplyCardStyle("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer", cardSurface, cardBorderStrong, 16, 1);
		ApplyCardStyle("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer", cardSurfaceInset, cardBorder, 14, 1);

		ApplyCardStyle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection", cardSurfaceAlt, WithAlpha(showcaseAccent, 0.40f), 16, 1);
		ApplyCardStyle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2", cardSurface, WithAlpha(recentAccent, 0.38f), 16, 1);
		ApplyCardStyle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends", cardSurfaceAlt, WithAlpha(friendsAccent, 0.40f), 16, 1);

		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/HSeparator", WithAlpha(showcaseAccent, 0.32f));
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/HSeparator", WithAlpha(recentAccent, 0.30f));
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/HSeparator", WithAlpha(friendsAccent, 0.30f));
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/GamesAndFriendsSeparator2", separatorColor);
		StyleSeparator("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/GamesAndFriendsSeparator", separatorColor);

		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/TileGrid",
			showcaseAccent,
			themeTileAccents);
		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/TileGrid",
			recentAccent,
			themeTileAccents);
		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/TileGrid",
			friendsAccent,
			themeTileAccents);

			StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/FooterRow/Button", chipSurface, showcaseAccent);
			StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/FooterRow/Button", chipSurface, recentAccent);
			StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/FooterRow/Button", chipSurface, friendsAccent);
			ApplyFriendDrawerTheme();
		}

	private void ApplySceneBackgroundShader()
	{
		if (GetNodeOrNull<ColorRect>("Bg") is not ColorRect bg)
			return;

		bg.Color = _visualStyle.Background.Overlay;
		if (bg.Material is not ShaderMaterial material)
			return;

		if (!bg.HasMeta(SceneBackgroundMaterialMeta))
		{
			material = material.Duplicate() as ShaderMaterial ?? material;
			bg.Material = material;
			bg.SetMeta(SceneBackgroundMaterialMeta, true);
		}

		material.SetShaderParameter("tint", _visualStyle.Background.SceneTint);
		material.SetShaderParameter("highlight_color", _visualStyle.Background.SceneHighlight);
		material.SetShaderParameter("highlight_thickness", 0.0f);
	}

	private void ApplyAvatarFrameLayout()
	{
		var frame = _visualStyle.AvatarFrame;
		var avatarSize = new Vector2(frame.AvatarSize, frame.AvatarSize);

		if (GetNodeOrNull<MarginContainer>(AvatarFrameMarginPath) is MarginContainer frameMargin)
			SetMarginConstants(frameMargin, frame.FrameMargin);

		if (GetNodeOrNull<MarginContainer>(AvatarInnerMarginPath) is MarginContainer innerMargin)
			SetMarginConstants(innerMargin, frame.InnerMargin);

		if (GetNodeOrNull<PanelContainer>(AvatarFramePath) is PanelContainer framePanel)
		{
			framePanel.ClipContents = true;
			framePanel.AddThemeStyleboxOverride(
				"panel",
				CreatePanelStyle(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f), frame.PanelRadius, 0));
		}

		if (GetNodeOrNull<PanelContainer>(AvatarCardPath) is PanelContainer avatarCard)
		{
			var existing = avatarCard.GetThemeStylebox("panel") as StyleBoxFlat;
			var background = existing?.BgColor ?? _visualStyle.Background.CardSurfaceAlt;
			var border = existing?.BorderColor ?? WithAlpha(_visualStyle.Background.Accent, 0.62f);
			var borderWidth = existing?.BorderWidthLeft ?? 1;
			avatarCard.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, frame.PanelRadius, borderWidth));
		}

		if (GetNodeOrNull<Control>(AvatarAspectPath) is Control aspect)
		{
			aspect.ClipContents = true;
			aspect.CustomMinimumSize = avatarSize;
		}

		if (GetNodeOrNull<Control>(AvatarStackPath) is Control stack)
		{
			stack.ClipContents = true;
			stack.CustomMinimumSize = avatarSize;
		}

		_avatar.ClipContents = true;
		_avatar.CustomMinimumSize = avatarSize;
		_avatar.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_avatar.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		_avatar.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_avatar.OffsetLeft = 0f;
		_avatar.OffsetTop = 0f;
		_avatar.OffsetRight = 0f;
		_avatar.OffsetBottom = 0f;
	}

	private static Color[] BuildCohesiveTileAccents(Color accent)
	{
		return new[]
		{
			WithAlpha(accent, 1f),
			WithAlpha(ShiftHue(accent, -0.11f, saturationBoost: 0.05f, valueBoost: 0.03f), 1f),
			WithAlpha(ShiftHue(accent, 0.14f, saturationBoost: 0.04f, valueBoost: 0.03f), 1f),
			WithAlpha(Mix(ShiftHue(accent, 0.24f, saturationBoost: 0.02f, valueBoost: 0.02f), accent, 0.25f), 1f)
		};
	}

	private void StyleTileGrid(string containerPath, Color sectionAccent, Color[] tileAccents)
	{
		var container = GetNodeOrNull<Node>(containerPath);
		if (container == null)
			return;

		int index = 0;
		foreach (Node child in container.GetChildren())
		{
			if (child is not Button button)
				continue;

			var accent = tileAccents[index % tileAccents.Length];
			ApplyTileTheme(button, sectionAccent, accent);
			index++;
		}
	}

		private void StyleFooterButton(string buttonPath, Color background, Color accent)
		{
			var button = GetNodeOrNull<Button>(buttonPath);
		if (button == null)
			return;

			button.Text = "See All";
			button.Alignment = HorizontalAlignment.Center;
			ApplyButtonTheme(button, background, accent, isChip: true);
			ApplyProfileHoverFeedback(button, scaleUp: 1.06f, shadowOffsetY: 3f);
		}

		private void ApplyFriendDrawerTheme()
		{
			if (_friendDrawerPanel == null)
				return;

			var panelSurface = _visualStyle.Background.CardSurface;
			var panelBorder = WithAlpha(_visualStyle.Background.Accent, 0.46f);
			var friendsAccent = WithAlpha(_visualStyle.Background.FriendsAccent, 0.92f);
			var chipSurface = _visualStyle.Background.ChipSurface;
			var tileAccents = _visualStyle.Background.TileAccents;

			_friendDrawerPanel.AddThemeStyleboxOverride("panel", CreatePanelStyle(panelSurface, panelBorder, 22, 1));

			UiStyle.StyleTitleLabel(_friendDrawerTitle);
			_friendDrawerTitle.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 1f, 0.99f));
			_friendDrawerTitle.AddThemeFontSizeOverride("font_size", 28);

			UiStyle.StyleMetaLabel(_friendDrawerCount);
			_friendDrawerCount.AddThemeFontSizeOverride("font_size", 14);
			_friendDrawerCount.AddThemeColorOverride("font_color", new Color(friendsAccent.R, friendsAccent.G, friendsAccent.B, 0.88f));

			ApplyButtonTheme(_friendDrawerClose, chipSurface, friendsAccent, isChip: true);
			ApplyProfileHoverFeedback(_friendDrawerClose, scaleUp: 1.06f, shadowOffsetY: 3f);

			int index = 0;
			foreach (Node child in _friendDrawerList.GetChildren())
			{
				switch (child)
				{
					case Button button:
						ApplyTileTheme(button, friendsAccent, tileAccents[index % tileAccents.Length]);
						index++;
						break;
					case Label label:
						UiStyle.StyleStatusLabel(label);
						label.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 1f, 0.84f));
						label.AddThemeFontSizeOverride("font_size", 20);
						break;
				}
			}
		}

	private void ApplyTileTheme(Button button, Color sectionAccent, Color tileAccent)
	{
		var baseSurface = Mix(new Color(0.13f, 0.11f, 0.20f, 0.96f), sectionAccent, 0.10f);
		var background = Mix(baseSurface, tileAccent, 0.22f);
		var hover = Mix(background, tileAccent, 0.12f);
		var pressed = Mix(background, tileAccent, 0.06f);
		var border = WithAlpha(tileAccent, 0.82f);
		var focusBorder = Mix(tileAccent, new Color(0.97f, 0.95f, 1f, 1f), 0.18f);

		button.Flat = false;
		button.Alignment = HorizontalAlignment.Left;
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			button.AddThemeFontSizeOverride("font_size", 14);
			button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, border, 2, 16, 10f, 9f));
			button.AddThemeStyleboxOverride("hover", CreateButtonStyle(hover, tileAccent, 2, 16, 10f, 9f));
			button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(pressed, tileAccent, 2, 16, 10f, 9f));
			button.AddThemeStyleboxOverride("focus", CreateButtonStyle(hover, focusBorder, 3, 16, 10f, 9f));
		button.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_focus_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		ApplyProfileHoverFeedback(button, scaleUp: 1.015f, duration: 0.10f, shadowOffsetY: 2f);
	}

	private static void ApplyProfileHoverFeedback(Button? button, float scaleUp = 1.04f, float duration = 0.11f, float shadowOffsetY = 3f)
	{
		if (button == null || button.HasMeta(HoverFeedbackAppliedMeta))
			return;

		UiStyle.AddHoverFeedback(button, scaleUp, duration);
		UiStyle.ApplyParallaxShadow(button, offsetY: shadowOffsetY);
		button.SetMeta(HoverFeedbackAppliedMeta, true);
	}

	private void StyleSectionTitle(string nodePath, Color color)
	{
		var label = GetNodeOrNull<Label>(nodePath);
		if (label == null)
			return;

		UiStyle.StyleTitleLabel(label);
		label.AddThemeColorOverride("font_color", new Color(color.R, color.G, color.B, 0.98f));
	}

	private void StyleSeparator(string nodePath, Color color)
	{
		if (GetNodeOrNull<CanvasItem>(nodePath) is CanvasItem separator)
			separator.Modulate = color;
	}

	private static void SetMarginConstants(MarginContainer margin, int value)
	{
		margin.AddThemeConstantOverride("margin_left", value);
		margin.AddThemeConstantOverride("margin_top", value);
		margin.AddThemeConstantOverride("margin_right", value);
		margin.AddThemeConstantOverride("margin_bottom", value);
	}

	private void ApplyCardStyle(string nodePath, Color background, Color border, int radius, int borderWidth)
	{
		var control = GetNodeOrNull<Control>(nodePath);
		if (control == null)
			return;

		control.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, radius, borderWidth));
	}

	private void ApplyButtonTheme(Button button, Color background, Color accent, bool isChip = false)
	{
		int borderWidth = 1;
		int radius = isChip ? 999 : 14;
		float horizontalPadding = isChip ? 10f : 9f;
		float verticalPadding = isChip ? 4f : 5f;
		var hover = Mix(background, accent, 0.11f);
		var pressed = Mix(background, accent, 0.05f);
		var focusBorder = Mix(accent, new Color(0.76f, 0.90f, 1f, 1f), 0.25f);

		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, WithAlpha(accent, isChip ? 0.70f : 0.56f), borderWidth, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(hover, accent, borderWidth, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(pressed, accent, borderWidth, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(hover, focusBorder, 2, radius, horizontalPadding, verticalPadding));
		button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.20f, 0.20f, 0.23f, 0.55f), new Color(0.52f, 0.52f, 0.56f, 0.5f), 1, radius, horizontalPadding, verticalPadding));
		button.AddThemeColorOverride("font_color", new Color(0.95f, 0.94f, 1f, 0.98f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.95f, 0.94f, 1f, 0.98f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.95f, 0.94f, 1f, 0.98f));
		button.AddThemeColorOverride("font_focus_color", new Color(0.95f, 0.94f, 1f, 0.98f));
	}

	private void StartBackgroundTransition(float duration = 1.5f)
	{
		var bg = GetNodeOrNull<GlobalBackground>("/root/GlobalBackground");
		if (bg == null)
		{
			GD.PrintErr("GlobalBackground node not found!");
			return;
		}

		try
		{
			bg.StartProfileBackgroundTransition(_visualStyle.Background.Id, duration);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Gradient transition failed: {ex.Message}");
		}
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

	private static Color ShiftHue(Color color, float hueOffset, float saturationBoost = 0f, float valueBoost = 0f)
	{
		RgbToHsv(color, out var hue, out var saturation, out var value);
		return HsvToRgb(
			Wrap01(hue + hueOffset),
			Clamp01(saturation + saturationBoost),
			Clamp01(value + valueBoost),
			color.A);
	}

	private static void RgbToHsv(Color color, out float hue, out float saturation, out float value)
	{
		var max = MathF.Max(color.R, MathF.Max(color.G, color.B));
		var min = MathF.Min(color.R, MathF.Min(color.G, color.B));
		var delta = max - min;

		value = max;
		saturation = max <= 0f ? 0f : delta / max;

		if (delta <= 0.00001f)
		{
			hue = 0f;
			return;
		}

		if (MathF.Abs(max - color.R) <= 0.00001f)
			hue = ((color.G - color.B) / delta) % 6f;
		else if (MathF.Abs(max - color.G) <= 0.00001f)
			hue = ((color.B - color.R) / delta) + 2f;
		else
			hue = ((color.R - color.G) / delta) + 4f;

		hue = Wrap01(hue / 6f);
	}

	private static Color HsvToRgb(float hue, float saturation, float value, float alpha)
	{
		var h = Wrap01(hue) * 6f;
		var c = value * saturation;
		var x = c * (1f - MathF.Abs((h % 2f) - 1f));
		var m = value - c;

		float r;
		float g;
		float b;
		if (h < 1f)
			(r, g, b) = (c, x, 0f);
		else if (h < 2f)
			(r, g, b) = (x, c, 0f);
		else if (h < 3f)
			(r, g, b) = (0f, c, x);
		else if (h < 4f)
			(r, g, b) = (0f, x, c);
		else if (h < 5f)
			(r, g, b) = (x, 0f, c);
		else
			(r, g, b) = (c, 0f, x);

		return new Color(r + m, g + m, b + m, alpha);
	}

	private static float Clamp01(float value)
	{
		return Math.Clamp(value, 0f, 1f);
	}

	private static float Wrap01(float value)
	{
		value %= 1f;
		return value < 0f ? value + 1f : value;
	}

	private static Color Mix(Color from, Color to, float amount)
	{
		return new Color(
			from.R + ((to.R - from.R) * amount),
			from.G + ((to.G - from.G) * amount),
			from.B + ((to.B - from.B) * amount),
			from.A + ((to.A - from.A) * amount));
	}

	private async void GoBack()
	{
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;

		if (string.Equals(returnScene, "res://profile.tscn", StringComparison.OrdinalIgnoreCase))
		{
			if (tree.HasMeta(SettingsParentReturnSceneMeta))
			{
				var parentScene = tree.GetMeta(SettingsParentReturnSceneMeta).AsString();
				if (!string.IsNullOrWhiteSpace(parentScene))
					returnScene = parentScene;

				tree.RemoveMeta(SettingsParentReturnSceneMeta);
			}
			else
			{
				returnScene = "res://HomeScreen.tscn";
			}
		}

		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;
		if (string.Equals(returnScene, "res://profile.tscn", StringComparison.OrdinalIgnoreCase))
			returnScene = "res://HomeScreen.tscn";
		tree.SetMeta("pgemu_return_scene", returnScene);

		await Transition.ChangeScene(returnScene,  ScreenTransition.TransitionType.Wipe, 0.25f, 0f);
	}

		private async void GoFriendsList()
		{
			ResetUiNavigationState();
			AudioManager.Instance?.PlaySelect();
			await OpenFriendDrawerAsync();
		}

	private void GoAchievements()
	{
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(1);
		AchievementStorage.gameId = -1;
		AchievementStorage.gameName = "Showcase";
		AchievementStorage.achievementData = null;
		var tree = GetTree();
		tree.SetMeta(ReturnSceneMetaKey, "res://profile.tscn");
		tree.ChangeSceneToFile("res://Achievements.tscn");
	}

	private async void GoGameSelect()
	{
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		CollectionStorage.currentCollection = await LoadRecentGameEntriesAsync(RecentGamesSeeAllLimit);
		CollectionStorage.SearchResult = null;
		tree.SetMeta(ReturnSceneMetaKey, "res://profile.tscn");
		await Transition.ChangeScene("res://GameSelect.tscn", ScreenTransition.TransitionType.Noise, 0.25f, 0.025f, false);
	}

	private async void OpenFriendProfileAsync(Button button)
	{
		if (!_friendTileUsernames.TryGetValue(button, out var username) || string.IsNullOrWhiteSpace(username))
		{
			if (button.HasMeta("pgemu_friend_username"))
				username = button.GetMeta("pgemu_friend_username").AsString();
			else
				username = button.Text;
		}

		username = username?.Trim();
		if (string.IsNullOrWhiteSpace(username))
			return;

		await OpenFriendProfileByUsernameAsync(username);
	}

	private async Task<bool> OpenFriendProfileByUsernameAsync(string username)
	{
		if (_openingFriendProfile)
			return false;
		if (string.IsNullOrWhiteSpace(username))
			return false;

		_openingFriendProfile = true;
		try
		{
			AudioManager.Instance?.PlaySelect();
			var profile = await _profileService.GetUserProfile(username.Trim());
			if (profile == null)
			{
				GD.PrintErr($"Could not load friend profile for {username}.");
				return false;
			}

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return false;

			Global.foundProfile = profile;
			var tree = GetTree();
			tree.SetMeta("pgemu_found_profile_username", profile.Username ?? string.Empty);
			tree.SetMeta("pgemu_found_profile_user_id", profile.UserId ?? string.Empty);
			tree.SetMeta("pgemu_return_scene", "res://profile.tscn");
			tree.ChangeSceneToFile("res://FoundUserProfile.tscn");
			return true;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Friend profile open failed: {exception.Message}");
			return false;
		}
		finally
		{
			_openingFriendProfile = false;
		}
	}

	private void GoProfileSettings()
	{
		ResetUiNavigationState();
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.SetMeta(SettingsParentReturnSceneMeta, returnScene);
		tree.SetMeta("pgemu_return_scene", "res://profile.tscn");
		tree.SetMeta("pgemu_settings_tab", "profile");
		tree.ChangeSceneToFile("res://Settings.tscn");
	}

	private async Task LoadAvatar(string url)
	{
		var texture = await LoadAvatarTextureAsync(url);
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !GodotObject.IsInstanceValid(_avatar))
			return;
		if (texture == null)
			return;

		_avatar.Texture = texture;
	}

	private static string NormalizeAvatarUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
			return string.Empty;

		return url.StartsWith("/", StringComparison.Ordinal)
			? $"http://localhost:5276{url}"
			: url;
	}

	private static string? TryResolveLocalAvatarPath(string avatarReference)
	{
		if (string.IsNullOrWhiteSpace(avatarReference))
			return null;

		var cleanReference = avatarReference.Split('?', 2)[0];
		if (!cleanReference.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
			return null;

		var projectDir = ProjectSettings.GlobalizePath("res://");
		var relativePath = cleanReference.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
		var candidates = new[]
		{
			Path.GetFullPath(Path.Combine(projectDir, "..", "PGEmu.backend", relativePath)),
			Path.GetFullPath(Path.Combine(projectDir, "..", "PGEmu.backend", "bin", "Debug", "net10.0", relativePath)),
			Path.GetFullPath(Path.Combine(projectDir, "..", "PGEmu.backend", "bin", "Release", "net10.0", relativePath))
		};

		foreach (var candidate in candidates)
		{
			if (File.Exists(candidate))
				return candidate;
		}

		return candidates[0];
	}
}
