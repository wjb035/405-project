using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PGEmu.Services;
using PGEmu.Services.Models;

public partial class FriendsList : Control
{	
	private const string FriendsListOwnerMeta = "pgemu_friends_list_owner_username";
	private const string DefaultReturnScene = "res://profile.tscn";
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private readonly ProfileService _profileService = new();
	private Button _back = null!;
	private Label _title = null!;
	private VBoxContainer _friendsContainer = null!;
	private bool _openingFriendProfile;
	private string _ownerUsername = string.Empty;
	
	
	// Called when the node enters the scene tree for the first time.
	public override async void _Ready()
	{
		_back = GetNode<Button>("Bg/Margin/Root/TopBar/BtnBack");
		_title = GetNode<Label>("Bg/Margin/Root/TopBar/Label");
		_friendsContainer = GetNode<VBoxContainer>("Bg/Margin/Root/PanelContainer2/MarginContainer/VBoxContainer");
		var tree = GetTree();
		_ownerUsername = tree.HasMeta(FriendsListOwnerMeta)
			? (tree.GetMeta(FriendsListOwnerMeta).AsString() ?? string.Empty).Trim()
			: string.Empty;

		if (!string.IsNullOrWhiteSpace(_ownerUsername))
			_title.Text = $"{_ownerUsername}'s Friends";
		else
			_title.Text = "Friends";

		if (!_back.IsConnected(Button.SignalName.Pressed, Callable.From(GoBack)))
			_back.Pressed += GoBack;

		ApplyThemeAesthetic();
		await PopulateFriendsAsync();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	
	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
	}
	
	
	private void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene")
			? tree.GetMeta("pgemu_return_scene").AsString()
			: null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? DefaultReturnScene : returnScene;
		if (tree.HasMeta(FriendsListOwnerMeta))
			tree.RemoveMeta(FriendsListOwnerMeta);
		tree.ChangeSceneToFile(returnScene);
	}

	private async Task PopulateFriendsAsync()
	{
		var friendNames = await LoadFriendNamesAsync();
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
			return;

		foreach (Node child in _friendsContainer.GetChildren())
			child.QueueFree();

		if (friendNames.Count == 0)
		{
			var emptyState = new Label
			{
				Text = "No friends yet",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0f, 80f)
			};
			emptyState.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 1f, 0.86f));
			emptyState.AddThemeFontSizeOverride("font_size", 20);
			_friendsContainer.AddChild(emptyState);
			return;
		}

		foreach (var username in friendNames)
		{
			var friendButton = new Button
			{
				CustomMinimumSize = new Vector2(0f, 60f),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				Text = username,
				Alignment = HorizontalAlignment.Left,
				ExpandIcon = true,
				TooltipText = $"View {username}'s profile"
			};

			friendButton.Pressed += async () => await OpenFriendProfileByUsernameAsync(username);
			_friendsContainer.AddChild(friendButton);
		}

		ApplyThemeAesthetic();
	}

	private async Task<IReadOnlyList<string>> LoadFriendNamesAsync()
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(_ownerUsername))
			{
				var response = await AuthService.Instance.SendAuthorizedRequest(
					$"http://localhost:5276/api/profile/{Uri.EscapeDataString(_ownerUsername)}/friends?limit=100");
				if (response == null || response.Value.ValueKind != JsonValueKind.Array)
					return Array.Empty<string>();

				var friends = JsonSerializer.Deserialize<List<UserSearchResultResponse>>(
					response.Value.GetRawText(),
					JsonOptions);

				return (friends ?? new List<UserSearchResultResponse>())
					.Select(friend => friend.Username?.Trim() ?? string.Empty)
					.Where(username => !string.IsNullOrWhiteSpace(username))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.OrderBy(username => username, StringComparer.OrdinalIgnoreCase)
					.ToArray();
			}

			var friendsJson = await FriendActivity.GetFriendsJson();
			if (string.IsNullOrWhiteSpace(friendsJson))
				return Array.Empty<string>();

			var friendsRoot = JsonSerializer.Deserialize<JsonElement>(friendsJson);
			if (friendsRoot.ValueKind != JsonValueKind.Array)
				return Array.Empty<string>();

			return friendsRoot.EnumerateArray()
				.Where(friend => friend.ValueKind == JsonValueKind.Object && IsAcceptedFriend(friend))
				.Select(friend => ReadJsonString(friend, "username"))
				.Where(username => !string.IsNullOrWhiteSpace(username))
				.Select(username => username.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(username => username, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Friend list load failed: {exception.Message}");
			return Array.Empty<string>();
		}
	}

	private async Task OpenFriendProfileByUsernameAsync(string username)
	{
		if (_openingFriendProfile || string.IsNullOrWhiteSpace(username))
			return;

		_openingFriendProfile = true;
		try
		{
			AudioManager.Instance?.PlaySelect();
			var profile = await _profileService.GetUserProfile(username.Trim());
			if (profile == null)
			{
				GD.PrintErr($"Could not load friend profile for {username}.");
				return;
			}

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			Global.foundProfile = profile;
			var tree = GetTree();
			tree.SetMeta("pgemu_found_profile_username", profile.Username ?? string.Empty);
			tree.SetMeta("pgemu_found_profile_user_id", profile.UserId ?? string.Empty);
			tree.SetMeta("pgemu_return_scene", "res://profile.tscn");
			tree.ChangeSceneToFile("res://FoundUserProfile.tscn");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Friend profile open failed: {exception.Message}");
		}
		finally
		{
			_openingFriendProfile = false;
		}
	}

	private static bool IsAcceptedFriend(JsonElement friend)
	{
		if (!friend.TryGetProperty("status", out var statusProperty))
			return true;

		return statusProperty.ValueKind switch
		{
			JsonValueKind.String => string.Equals(statusProperty.GetString(), "Accepted", StringComparison.OrdinalIgnoreCase),
			JsonValueKind.Number => statusProperty.TryGetInt32(out int value) && value == 1,
			_ => false,
		};
	}

	private static string ReadJsonString(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out var property))
			return string.Empty;

		return property.ValueKind switch
		{
			JsonValueKind.String => property.GetString() ?? string.Empty,
			JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.GetRawText(),
			_ => string.Empty,
		};
	}

	private void ApplyThemeAesthetic()
	{
		if (GetNodeOrNull<ColorRect>("Bg") is ColorRect bg)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 0.96f);

		var cardSurface = new Color(0.12f, 0.10f, 0.19f, 0.95f);
		var cardBorder = new Color(1f, 0.73f, 0.86f, 0.40f);
		var chipSurface = new Color(0.17f, 0.14f, 0.27f, 0.92f);
		var chipAccent = new Color(0.47f, 0.84f, 1f, 0.90f);
		var listAccent = new[]
		{
			new Color(1f, 0.73f, 0.86f, 1f),
			new Color(0.52f, 0.87f, 1f, 1f),
			new Color(1f, 0.81f, 0.48f, 1f),
			new Color(0.72f, 0.89f, 0.67f, 1f)
		};

		UiStyle.StyleTopBarButton(_back);
		UiStyle.AddHoverFeedback(_back);
		UiStyle.ApplyParallaxShadow(_back);
		UiStyle.TightenButtonContentPadding(_back, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_back, chipSurface, chipAccent, isChip: true);

		UiStyle.StyleTitleLabel(_title);
		_title.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 1f, 0.98f));

		ApplyCardStyle("Bg/Margin/Root/PanelContainer2", cardSurface, cardBorder, 16, 1);

		var listContainer = GetNodeOrNull<Node>("Bg/Margin/Root/PanelContainer2/MarginContainer/VBoxContainer");
		if (listContainer == null)
			return;

		int index = 0;
		foreach (Node child in listContainer.GetChildren())
		{
			if (child is not Button button)
				continue;

			var accent = listAccent[index % listAccent.Length];
			ApplyTileTheme(button, accent);
			index++;
		}
	}

	private void ApplyCardStyle(string nodePath, Color background, Color border, int radius, int borderWidth)
	{
		if (GetNodeOrNull<Control>(nodePath) is not Control control)
			return;

		control.AddThemeStyleboxOverride("panel", CreatePanelStyle(background, border, radius, borderWidth));
	}

	private static void ApplyButtonTheme(Button button, Color background, Color accent, bool isChip = false)
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

	private static void ApplyTileTheme(Button button, Color accent)
	{
		var background = Mix(new Color(0.13f, 0.11f, 0.20f, 0.96f), accent, 0.22f);
		var hover = Mix(background, accent, 0.12f);
		var pressed = Mix(background, accent, 0.06f);
		var border = WithAlpha(accent, 0.82f);
		var focusBorder = Mix(accent, new Color(0.97f, 0.95f, 1f, 1f), 0.18f);

		button.Flat = false;
		button.Alignment = HorizontalAlignment.Left;
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.AddThemeFontSizeOverride("font_size", 14);
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, border, 2, 16, 10f, 9f));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(hover, accent, 2, 16, 10f, 9f));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(pressed, accent, 2, 16, 10f, 9f));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(hover, focusBorder, 3, 16, 10f, 9f));
		button.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_focus_color", new Color(0.97f, 0.95f, 1f, 0.98f));
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
}
