using Godot;
using System;
using PGEmu.app;
using System.Collections.Generic;
using RetroAchievements.Api;
using RetroAchievements.Api.Response.Users.Records;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PGEmu.Services;


public partial class AchievementScreen : Control
{
	private const string MediaBaseUrl = "https://retroachievements.org";
	
	private Button _back;
	
	public override async void _Ready()
	{
		Label _topText = GetNode<Label>("Margin/Root/TopBar/Title");
		_back = GetNode<Button>("Margin/Root/TopBar/BtnBack");
		string splashText = AchievementStorage.gameId == -1
			? "Showcase"
			: "Achievements for " + AchievementStorage.gameName;
		
		string regexPattern =  @"\([^)]*\)";
		splashText = Regex.Replace(splashText, regexPattern, String.Empty);
		
		
		_topText.Text = splashText;
		_back.Pressed += GoBack;
		ApplyAesthetic();
		
		// Get the container
		VBoxContainer container = GetNode<VBoxContainer>("ScrollContainer/ButtonContainer");
		if (AchievementStorage.gameId == -1)
		{
			await PopulateUserAwardsAsync(container, _topText);
			return;
		}

		if (AchievementStorage.gameId != -1){
			await RetroAchievementsService.achievementGet(AchievementStorage.gameId);
		}
		else{
			//await RetroAchievementsService.achievementGet(2689);
		}
		
		
		
		var icons = new List<String>();
		var iconsAndAchData = new List<KeyValuePair<string, UserProgressAchievement>>();
		if (AchievementStorage.achievementData != null){
			foreach (var g in AchievementStorage.achievementData)
			{
			
				iconsAndAchData.Add(
				new KeyValuePair<string, UserProgressAchievement>
				("https://media.retroachievements.org/Badge/"+g.Value.BadgeName+".png", g.Value));
				//GD.Print(g.Value.Title + " has an id of " + g.Key + " with a badge url of " + g.Value.BadgeName + " unlocked on " + g.Value.EarnedDate);
				//icons.Add("https://media.retroachievements.org/Badge/"+g.Value.BadgeName+".png");
			}
		}
		
		iconsAndAchData = iconsAndAchData.OrderByDescending(x => x.Value.EarnedDate).ToList();
		

		for (int i = 0; i < iconsAndAchData.Count; i++)
{
	
Button btn = new Button();
btn.SizeFlagsHorizontal = SizeFlags.Fill;
btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
btn.CustomMinimumSize = new Vector2(1000, 100); // fixed height like you want
UiStyle.StyleTopBarButton(btn);

int index = i;
btn.Pressed += () =>
{
	AudioManager.Instance?.PlayClick();
	GD.Print($"Button {index + 1} pressed");
};

	HBoxContainer rootRow = new HBoxContainer();
	rootRow.SizeFlagsHorizontal = SizeFlags.Fill;
	rootRow.Alignment = BoxContainer.AlignmentMode.Begin;
	rootRow.AddThemeConstantOverride("separation", 14); // slightly tighter
	btn.AddChild(rootRow);

	MarginContainer iconWrapper = new MarginContainer();
	iconWrapper.AddThemeConstantOverride("margin_left", 10);
	iconWrapper.AddThemeConstantOverride("margin_top", 16);
	rootRow.AddChild(iconWrapper);


	TextureRect icon = new TextureRect();
	icon.CustomMinimumSize = new Vector2(48, 48);
	icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
	iconWrapper.AddChild(icon);

	Label titleLabel = new Label();
	titleLabel.Text = iconsAndAchData[i].Value.Title;
	titleLabel.CustomMinimumSize = new Vector2(360, 0);
	titleLabel.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
	titleLabel.AutowrapMode = TextServer.AutowrapMode.Word;
	titleLabel.MaxLinesVisible = 2;
	titleLabel.VerticalAlignment = VerticalAlignment.Top;
	
	
	
	rootRow.AddChild(titleLabel);
	
	

	Control spacer = new Control();
	spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
	spacer.SizeFlagsStretchRatio = 0.5f;
	rootRow.AddChild(spacer);

	Label dateLabel = new Label();
	bool unlocked = iconsAndAchData[i].Value.EarnedDate.Year > 1;
	dateLabel.Text = unlocked
		? iconsAndAchData[i].Value.EarnedDate.ToString()
		: "Not unlocked!";
	dateLabel.CustomMinimumSize = new Vector2(165, 0);
	dateLabel.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
	dateLabel.VerticalAlignment = VerticalAlignment.Top;
	dateLabel.HorizontalAlignment = HorizontalAlignment.Left;
	dateLabel.AddThemeFontSizeOverride("font_size", 12);
	dateLabel.Modulate = new Color(0.65f, 0.65f, 0.65f);
	rootRow.AddChild(dateLabel);

	ColorRect separator = new ColorRect();
	separator.Color = new Color(1, 1, 1, 0.6f);
	separator.CustomMinimumSize = new Vector2(2, 70);
	separator.SizeFlagsVertical = SizeFlags.ShrinkCenter;
	rootRow.AddChild(separator);

	Label subtitleLabel = new Label();
	subtitleLabel.Text = iconsAndAchData[i].Value.Description;
	subtitleLabel.CustomMinimumSize = new Vector2(250, 0);
	subtitleLabel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
	subtitleLabel.AutowrapMode = TextServer.AutowrapMode.Word;
	subtitleLabel.MaxLinesVisible = 4;
	subtitleLabel.VerticalAlignment = VerticalAlignment.Top;
	subtitleLabel.AddThemeFontSizeOverride("font_size", 14);
	subtitleLabel.Modulate = new Color(0.8f, 0.8f, 0.8f);

	rootRow.AddChild(subtitleLabel);
	btn.AddThemeConstantOverride("content_margin_top", 6);
	btn.AddThemeConstantOverride("content_margin_bottom", 2);

	// add to container
	container.AddChild(btn);

	// Load icon asynchronously
	LoadIconFromUrl(icon, iconsAndAchData[i].Key, unlocked);
}
if (iconsAndAchData.Count == 0){
	_topText.Text ="No achievements found for this game";
}



	}

	private async Task PopulateUserAwardsAsync(VBoxContainer container, Label topText)
	{
		if (string.IsNullOrWhiteSpace(RetroAchievementsService.username) ||
			string.IsNullOrWhiteSpace(RetroAchievementsService.apiKey))
		{
			topText.Text = "Connect RetroAchievements in Settings";
			return;
		}

		try
		{
			var response = await RetroAchievementsService.client.GetUserAwardsAsync(RetroAchievementsService.username);
			var awards = response?.VisibleUserAwards?
				.Where(IsShowcaseAward)
				.OrderBy(award => award.DisplayOrder)
				.ToList() ?? new List<VisibleUserAward>();

			if (awards.Count == 0)
			{
				topText.Text = "No showcase trophies found";
				return;
			}

			topText.Text = "Showcase";
			foreach (var award in awards)
				AddAwardRow(container, award);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"Showcase awards load failed: {exception.Message}");
			topText.Text = "Invalid API Key or RetroAchievements Username";
		}
	}

	private static bool IsShowcaseAward(VisibleUserAward award)
	{
		return string.Equals(award.AwardType, "Game Beaten", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(award.AwardType, "Mastery/Completion", StringComparison.OrdinalIgnoreCase);
	}

	private void AddAwardRow(VBoxContainer container, VisibleUserAward award)
	{
		var btn = new Button();
		btn.SizeFlagsHorizontal = SizeFlags.Fill;
		btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		btn.CustomMinimumSize = new Vector2(1000, 100);
		UiStyle.StyleTopBarButton(btn);

		btn.Pressed += () =>
		{
			AudioManager.Instance?.PlayClick();
		};

		var rootRow = new HBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.Fill,
			Alignment = BoxContainer.AlignmentMode.Begin
		};
		rootRow.AddThemeConstantOverride("separation", 14);
		btn.AddChild(rootRow);

		var iconWrapper = new MarginContainer();
		iconWrapper.AddThemeConstantOverride("margin_left", 10);
		iconWrapper.AddThemeConstantOverride("margin_top", 16);
		rootRow.AddChild(iconWrapper);

		var icon = new TextureRect
		{
			CustomMinimumSize = new Vector2(48, 48),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
		};
		iconWrapper.AddChild(icon);

		var trophyType = string.Equals(award.AwardType, "Mastery/Completion", StringComparison.OrdinalIgnoreCase)
			? "Mastery"
			: "Beaten";

		var titleLabel = new Label
		{
			Text = $"{trophyType}: {award.Title}",
			CustomMinimumSize = new Vector2(360, 0),
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
			AutowrapMode = TextServer.AutowrapMode.Word,
			MaxLinesVisible = 2,
			VerticalAlignment = VerticalAlignment.Top
		};
		rootRow.AddChild(titleLabel);

		var spacer = new Control
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsStretchRatio = 0.5f
		};
		rootRow.AddChild(spacer);

		var dateLabel = new Label
		{
			Text = award.AwardedDate.ToString("g"),
			CustomMinimumSize = new Vector2(165, 0),
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
			VerticalAlignment = VerticalAlignment.Top,
			HorizontalAlignment = HorizontalAlignment.Left,
			Modulate = new Color(0.65f, 0.65f, 0.65f)
		};
		dateLabel.AddThemeFontSizeOverride("font_size", 12);
		rootRow.AddChild(dateLabel);

		var separator = new ColorRect
		{
			Color = new Color(1, 1, 1, 0.6f),
			CustomMinimumSize = new Vector2(2, 70),
			SizeFlagsVertical = SizeFlags.ShrinkCenter
		};
		rootRow.AddChild(separator);

		var subtitleLabel = new Label
		{
			Text = string.IsNullOrWhiteSpace(award.ConsoleName)
				? award.AwardType
				: $"{award.ConsoleName} | {award.AwardType}",
			CustomMinimumSize = new Vector2(250, 0),
			SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
			AutowrapMode = TextServer.AutowrapMode.Word,
			MaxLinesVisible = 4,
			VerticalAlignment = VerticalAlignment.Top,
			Modulate = new Color(0.8f, 0.8f, 0.8f)
		};
		subtitleLabel.AddThemeFontSizeOverride("font_size", 14);
		rootRow.AddChild(subtitleLabel);

		btn.AddThemeConstantOverride("content_margin_top", 6);
		btn.AddThemeConstantOverride("content_margin_bottom", 2);
		container.AddChild(btn);

		var iconUrl = BuildMediaUrl(award.ImageIcon);
		if (!string.IsNullOrWhiteSpace(iconUrl))
			LoadIconFromUrl(icon, iconUrl, true);
	}

	private static string? BuildMediaUrl(string? siteRelativePath)
	{
		if (string.IsNullOrWhiteSpace(siteRelativePath))
			return null;

		if (siteRelativePath.StartsWith("file:///Images/", StringComparison.OrdinalIgnoreCase))
			siteRelativePath = siteRelativePath["file://".Length..];

		if (Uri.TryCreate(siteRelativePath, UriKind.Absolute, out var absoluteUri))
			return absoluteUri.ToString();

		var normalizedPath = siteRelativePath.StartsWith("/", StringComparison.Ordinal)
			? siteRelativePath
			: "/" + siteRelativePath;
		return $"{MediaBaseUrl}{normalizedPath}";
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
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://GameSelect.tscn" : returnScene;

		tree.ChangeSceneToFile(returnScene);
	}
	
	// fix this later
	private void LoadIconFromUrl(TextureRect textureRect, string url,
	bool unlocked)
{
	HttpRequest request = new HttpRequest();
	AddChild(request);

	
	request.RequestCompleted += (long result, long responseCode, string[] headers, byte[] body) =>
	{
		if (body.Length == 0)
		{
			GD.PrintErr($"Failed to download image from {url}");
			request.QueueFree();
			return;
		}

		
		Image img = new Image();
		Error err = img.LoadPngFromBuffer(body); 
		if (err != Error.Ok)
		{
			GD.PrintErr($"Failed to load image from buffer: {url}");
			request.QueueFree();
			return;
		}

		
		if (!unlocked)
		{
			for (int y = 0; y < img.GetHeight(); y++)
			{
				for (int x = 0; x < img.GetWidth(); x++)
				{
					Color color = img.GetPixel(x, y);
					float gray = color.R * 0.299f + color.G * 0.587f + color.B * 0.114f;
					img.SetPixel(x, y, new Color(gray, gray, gray, color.A));
				}
			}
		}
			
		ImageTexture tex = ImageTexture.CreateFromImage(img);
		textureRect.Texture = tex;

		
		request.QueueFree();
	};

	
	var errRequest = request.Request(url);
	if (errRequest != Error.Ok)
		GD.PrintErr($"Failed to start HTTP request: {url}");
	
}

	private void ApplyAesthetic()
	{
		var title = GetNodeOrNull<Label>("Margin/Root/TopBar/Title");
		UiStyle.StyleTitleLabel(title);
		UiStyle.StyleTopBarButton(_back);
		UiStyle.AddHoverFeedback(_back);
		UiStyle.ApplyParallaxShadow(_back);
	}
}
