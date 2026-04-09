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

public partial class Profile : Control
{
	private const int MaxTileCount = 4;
	private const string SettingsParentReturnSceneMeta = "pgemu_profile_settings_parent_return_scene";
	private const string ShowcaseTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/TileGrid";
	private const string RecentGamesTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/TileGrid";
	private const string FriendsTileGridPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/TileGrid";
	private const string RecentGamesFooterPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/FooterRow";
	private const string FriendsFooterPath = "Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/FooterRow";

	[Export] public NodePath BackPath;
	[Export] public NodePath AvatarPath;
	[Export] public NodePath ProfileSettingsShortcutPath;

	private readonly ProfileService _profileService = new();
	private readonly System.Net.Http.HttpClient _client = new();
	private readonly Dictionary<Button, string> _friendTileUsernames = new();

	private Button _back = null!;
	private Button _friendsList = null!;
	private Button _profileSettingsShortcut = null!;
	private Label _gamerTag = null!;
	private Label _profileNote = null!;
	private OptionButton _visibilityToggle = null!;
	private TextureRect _avatar = null!;

	private ProfileResponse? _profile;
	private bool _openingFriendProfile;

	public override async void _Ready()
	{
		_back = GetNode<Button>(BackPath);
		_profileSettingsShortcut = GetNode<Button>(ProfileSettingsShortcutPath);
		_friendsList = GetNode<Button>("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/FooterRow/Button");
		_gamerTag = GetNode<Label>("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/HeaderRow/GamerTag");
		_profileNote = GetNode<Label>("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/PanelContainer/MarginContainer/ProfileNote");
		_visibilityToggle = GetNode<OptionButton>("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer/MarginContainer/VBoxContainer/HeaderRow/OptionButton");
		_avatar = GetNode<TextureRect>(AvatarPath);

		ConnectIfNeeded(_back, GoBack);
		ConnectIfNeeded(_profileSettingsShortcut, GoProfileSettings);
		ConnectIfNeeded(_friendsList, GoFriendsList);
		ConnectFriendTileButtons();

		ApplyThemeAesthetic();
		SetLoadingState();
		await Task.WhenAll(
			LoadProfileAsync(),
			LoadSectionDataAsync());
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!ControllerService.TryHandleBackAction(@event, GoBack))
			return;

		GetViewport()?.SetInputAsHandled();
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

	private async Task LoadSectionDataAsync()
	{
		try
		{
			var showcaseTask = LoadCollectionNamesAsync();
			var recentGamesTask = LoadRecentGameLabelsAsync();
			var friendsTask = LoadFriendNamesAsync();

			await Task.WhenAll(showcaseTask, recentGamesTask, friendsTask);
			var showcaseItems = await showcaseTask;
			var recentGameItems = await recentGamesTask;
			var friendItems = await friendsTask;

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			ApplyTileContent(ShowcaseTileGridPath, showcaseItems, "No collections yet");
			ApplyTileContent(RecentGamesTileGridPath, recentGameItems, "No games found");
			ApplyFriendTileContent(friendItems, "No friends yet");

			SetFooterVisible(RecentGamesFooterPath, false);
			SetFooterVisible(FriendsFooterPath, true);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Profile sections failed to load: {exception.Message}");

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			ApplyTileContent(ShowcaseTileGridPath, Array.Empty<string>(), "No collections yet");
			ApplyTileContent(RecentGamesTileGridPath, Array.Empty<string>(), "No games found");
			ApplyFriendTileContent(Array.Empty<string>(), "No friends yet");
			SetFooterVisible(RecentGamesFooterPath, false);
			SetFooterVisible(FriendsFooterPath, true);
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

	private async Task<IReadOnlyList<string>> LoadCollectionNamesAsync()
	{
		try
		{
			var collectionsPath = ProjectSettings.GlobalizePath("res://collections.json");
			if (!File.Exists(collectionsPath))
				return Array.Empty<string>();

			await using var stream = File.OpenRead(collectionsPath);
			var collections = await JsonSerializer.DeserializeAsync<List<KeyValuePair<string, List<GameEntry>>>>(
				stream,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

			if (collections == null)
				return Array.Empty<string>();

			return collections
				.Select(collection => collection.Key?.Trim())
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.Take(MaxTileCount)
				.Cast<string>()
				.ToArray();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Collection load failed: {exception.Message}");
			return Array.Empty<string>();
		}
	}

	private async Task<IReadOnlyList<string>> LoadRecentGameLabelsAsync()
	{
		try
		{
			var installedGames = LoadInstalledGames();
			var playtimeLookup = await LoadPlaytimeLookupAsync();

			foreach (var game in installedGames)
				game.TimePlayed = ResolveTrackedPlaytime(playtimeLookup, game);

			var recentGames = installedGames
				.GroupBy(GetGameIdentity, StringComparer.OrdinalIgnoreCase)
				.Select(group => group
					.OrderByDescending(game => game.TimePlayed)
					.ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
					.First())
				.OrderByDescending(game => game.TimePlayed > 0)
				.ThenByDescending(game => game.TimePlayed)
				.ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
				.Take(MaxTileCount)
				.Select(FormatRecentGameLabel)
				.ToArray();

			if (recentGames.Length > 0)
				return recentGames;

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

	private async Task<IReadOnlyList<string>> LoadFriendNamesAsync()
	{
		try
		{
			var friendsJson = await FriendActivity.GetFriendsJson();
			if (string.IsNullOrWhiteSpace(friendsJson))
				return Array.Empty<string>();

			var friendsRoot = JsonSerializer.Deserialize<JsonElement>(friendsJson);
			if (friendsRoot.ValueKind != JsonValueKind.Array)
				return Array.Empty<string>();

			return friendsRoot.EnumerateArray()
				.Select(friend => ReadJsonString(friend, "username"))
				.Where(username => !string.IsNullOrWhiteSpace(username))
				.Take(MaxTileCount)
				.ToArray();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Friend list load failed: {exception.Message}");
			return Array.Empty<string>();
		}
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

		if (!lookup.TryGetValue(key, out var existing) || game.TimePlayed > existing.TimePlayed)
			lookup[key] = game;
	}

	private static string NormalizeGameName(string? name)
	{
		return string.IsNullOrWhiteSpace(name)
			? string.Empty
			: name.Trim().ToLowerInvariant();
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
		if (GetNodeOrNull<ColorRect>("Bg") is ColorRect bg)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 0.96f);

		var cardSurface = new Color(0.10f, 0.09f, 0.16f, 0.93f);
		var cardSurfaceAlt = new Color(0.12f, 0.10f, 0.19f, 0.95f);
		var cardSurfaceInset = new Color(0.15f, 0.13f, 0.23f, 0.96f);
		var cardBorder = new Color(0.56f, 0.48f, 0.76f, 0.46f);
		var cardBorderStrong = new Color(0.72f, 0.64f, 0.92f, 0.62f);
		var avatarBorder = new Color(0.48f, 0.83f, 1f, 0.48f);
		var chipSurface = new Color(0.17f, 0.14f, 0.27f, 0.92f);
		var chipAccent = new Color(0.74f, 0.84f, 1f, 0.84f);
		var showcaseAccent = new Color(0.47f, 0.84f, 1f, 0.90f);
		var recentAccent = new Color(0.57f, 0.97f, 0.79f, 0.88f);
		var friendsAccent = new Color(1f, 0.73f, 0.86f, 0.90f);
		var separatorColor = new Color(0.70f, 0.62f, 0.90f, 0.24f);

		UiStyle.StyleTopBarButton(_back);
		UiStyle.TightenButtonContentPadding(_back, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_back, chipSurface, showcaseAccent, isChip: true);
		UiStyle.StylePopupMenu(_visibilityToggle.GetPopup());
		UiStyle.StyleTopBarButton(_profileSettingsShortcut);
		UiStyle.TightenButtonContentPadding(_profileSettingsShortcut, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_profileSettingsShortcut, chipSurface, friendsAccent, isChip: true);

		UiStyle.StyleOptionButton(_visibilityToggle);
		UiStyle.TightenButtonContentPadding(_visibilityToggle, horizontal: 8f, vertical: 3f);
		ApplyButtonTheme(_visibilityToggle, chipSurface, chipAccent, isChip: true);

		if (GetNodeOrNull<Label>("Margin/Root/TopBar/Title") is Label title)
		{
			UiStyle.StyleTitleLabel(title);
			title.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 1f, 0.98f));
		}
		UiStyle.StyleTitleLabel(_gamerTag);
		UiStyle.StyleStatusLabel(_profileNote);

		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/ShowcaseSection/ShowcaseMargin/VBoxContainer/Showcase", showcaseAccent);
		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/Label", recentAccent);
		StyleSectionTitle("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/Label", friendsAccent);

		ApplyCardStyle("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer2", cardSurfaceAlt, avatarBorder, 18, 1);
		ApplyCardStyle("Margin/Root/BodyScroll/Body/MarginContainer/GridContainer/PanelContainer2/AvatarFrameMargin/AvatarFrame", cardSurfaceInset, cardBorderStrong, 16, 1);
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
			new[]
			{
				new Color(0.30f, 0.73f, 1f, 1f),
				new Color(1f, 0.78f, 0.42f, 1f),
				new Color(1f, 0.58f, 0.79f, 1f),
				new Color(0.61f, 0.90f, 0.66f, 1f)
			});
		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/TileGrid",
			recentAccent,
			new[]
			{
				new Color(0.58f, 0.97f, 0.79f, 1f),
				new Color(0.48f, 0.84f, 1f, 1f),
				new Color(0.90f, 0.77f, 1f, 1f),
				new Color(1f, 0.83f, 0.51f, 1f)
			});
		StyleTileGrid(
			"Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/TileGrid",
			friendsAccent,
			new[]
			{
				new Color(1f, 0.73f, 0.86f, 1f),
				new Color(0.52f, 0.87f, 1f, 1f),
				new Color(1f, 0.81f, 0.48f, 1f),
				new Color(0.72f, 0.89f, 0.67f, 1f)
			});

		StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/RecentGames2/MarginContainer/VBoxContainer/FooterRow/Button", chipSurface, recentAccent);
		StyleFooterButton("Margin/Root/BodyScroll/Body/RecentGamesAndFriends/Friends/MarginContainer/VBoxContainer/FooterRow/Button", chipSurface, friendsAccent);
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
		button.AddThemeFontSizeOverride("font_size", 15);
		button.AddThemeStyleboxOverride("normal", CreateButtonStyle(background, border, 2, 16, 12f, 11f));
		button.AddThemeStyleboxOverride("hover", CreateButtonStyle(hover, tileAccent, 2, 16, 12f, 11f));
		button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(pressed, tileAccent, 2, 16, 12f, 11f));
		button.AddThemeStyleboxOverride("focus", CreateButtonStyle(hover, focusBorder, 3, 16, 12f, 11f));
		button.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_hover_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.97f, 0.95f, 1f, 0.98f));
		button.AddThemeColorOverride("font_focus_color", new Color(0.97f, 0.95f, 1f, 0.98f));
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

	private void GoBack()
	{
		AudioManager.Instance?.PlayNavigation(-1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;

		if (string.Equals(returnScene, "res://profile.tscn", StringComparison.OrdinalIgnoreCase) &&
			tree.HasMeta(SettingsParentReturnSceneMeta))
		{
			var parentScene = tree.GetMeta(SettingsParentReturnSceneMeta).AsString();
			if (!string.IsNullOrWhiteSpace(parentScene))
				returnScene = parentScene;

			tree.RemoveMeta(SettingsParentReturnSceneMeta);
		}

		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;
		tree.SetMeta("pgemu_return_scene", returnScene);

		tree.ChangeSceneToFile(returnScene);
	}

	private void GoFriendsList()
	{
		AudioManager.Instance?.PlaySelect();
		GetTree().ChangeSceneToFile("res://FriendsList.tscn");
	}

	private async void OpenFriendProfileAsync(Button button)
	{
		if (_openingFriendProfile)
			return;

		if (!_friendTileUsernames.TryGetValue(button, out var username) || string.IsNullOrWhiteSpace(username))
			return;

		_openingFriendProfile = true;
		try
		{
			AudioManager.Instance?.PlaySelect();
			var profile = await _profileService.GetUserProfile(username);
			if (profile == null)
			{
				GD.PrintErr($"Could not load friend profile for {username}.");
				return;
			}

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree())
				return;

			Global.foundProfile = profile;
			var tree = GetTree();
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

	private void GoProfileSettings()
	{
		AudioManager.Instance?.PlayNavigation(1);
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

		tree.SetMeta(SettingsParentReturnSceneMeta, returnScene);
		tree.SetMeta("pgemu_return_scene", "res://profile.tscn");
		tree.ChangeSceneToFile("res://Settings.tscn");
	}

	private async Task LoadAvatar(string url)
	{
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
				if (url.StartsWith("/"))
					url = $"http://localhost:5276{url}";

				byte[] imageData = await _client.GetByteArrayAsync(url);
				err = avatar.LoadPngFromBuffer(imageData);
				if (err != Error.Ok)
					err = avatar.LoadJpgFromBuffer(imageData);
			}

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !GodotObject.IsInstanceValid(_avatar))
				return;

			if (err != Error.Ok)
			{
				GD.PrintErr("Failed to decode avatar image");
				return;
			}

			var texture = ImageTexture.CreateFromImage(avatar);
			_avatar.Texture = texture;
		}
		catch (Exception exception)
		{
			GD.PrintErr("Failed to load image: " + exception.Message);
		}
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
		return Path.GetFullPath(Path.Combine(projectDir, "..", "PGEmu.backend", relativePath));
	}
}
