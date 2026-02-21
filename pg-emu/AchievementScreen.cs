using Godot;
using System;
using PGEmu.app;
using System.Collections.Generic;
using RetroAchievements.Api.Response.Users.Records;
using System.Linq;
using System.Text.RegularExpressions;


public partial class AchievementScreen : Control
{
	
	
	public override async void _Ready()
	{
		Label _topText = GetNode<Label>("Bg/Margin/Root/TopBar/Title");
		Button back = GetNode<Button>("Bg/Margin/Root/TopBar/BtnBack");
		string splashText = "Achievements for " + AchievementStorage.gameName;
		
		string regexPattern =  @"\([^)]*\)";
		splashText = Regex.Replace(splashText, regexPattern, String.Empty);
		
		
		_topText.Text = splashText;
		back.Pressed += GoBack;
		
		// Get the container
		VBoxContainer container = GetNode<VBoxContainer>("ScrollContainer/ButtonContainer");
		
		
		
		if (AchievementStorage.gameId != -1){
			await RetroAchievementsService.achievementGet(AchievementStorage.gameId);
		}
		else{
			await RetroAchievementsService.achievementGet(2689);
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
	// Create main button
	Button btn = new Button();
	btn.SizeFlagsHorizontal = SizeFlags.Fill;      // stretch horizontally
	btn.SizeFlagsVertical = SizeFlags.ShrinkCenter; // height controlled by CustomMinimumSize
	btn.CustomMinimumSize = new Vector2(400, 60);  // button size
	int index = i;
	btn.Pressed += () => GD.Print($"Button {index + 1} pressed");

	// HBoxContainer inside button
	HBoxContainer hbox = new HBoxContainer();
	hbox.SizeFlagsHorizontal = SizeFlags.Fill;
	hbox.SizeFlagsVertical = SizeFlags.Fill;
	btn.AddChild(hbox);

	// Icon TextureRect
	TextureRect icon = new TextureRect();
	icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; 
	icon.SizeFlagsVertical = SizeFlags.Fill;         // fills button height
	icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered; // scale proportionally
	icon.CustomMinimumSize = new Vector2(48, 48);    // keeps it at least this big
	hbox.AddChild(icon);

	// Spacer between icon and text
	Control spacer = new Control();
	spacer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
	spacer.CustomMinimumSize = new Vector2(10, 0);
	hbox.AddChild(spacer);

	// VBox for text (title + subtitle)
VBoxContainer textBox = new VBoxContainer();
textBox.SizeFlagsHorizontal = SizeFlags.Fill;
textBox.SizeFlagsVertical = SizeFlags.Fill;
hbox.AddChild(textBox);

// First Label (Title)
Label titleLabel = new Label();
titleLabel.Text = iconsAndAchData[i].Value.Title;
titleLabel.SizeFlagsHorizontal = SizeFlags.Fill;
titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
textBox.AddChild(titleLabel);

// Second Label (Subtitle / Description)
Label subtitleLabel = new Label();
subtitleLabel.Text = iconsAndAchData[i].Value.Description; // or whatever field you use
subtitleLabel.SizeFlagsHorizontal = SizeFlags.Fill;
subtitleLabel.HorizontalAlignment = HorizontalAlignment.Left;
textBox.AddChild(subtitleLabel);


Label dateLabel = new Label();
dateLabel.Text = iconsAndAchData[i].Value.EarnedDate.ToString(); // or whatever field you use
bool unlocked = true;
if (dateLabel.Text == "1/1/0001 12:00:00 AM"){
	dateLabel.Text = "Not unlocked!";
	unlocked = false;
}

dateLabel.SizeFlagsHorizontal = SizeFlags.Fill;
dateLabel.HorizontalAlignment = HorizontalAlignment.Left;
textBox.AddChild(dateLabel);

// Optional: make it visually secondary
//subtitleLabel.Modulate = new Color(0.8f, 0.8f, 0.8f); // slightly dimmer
subtitleLabel.AddThemeFontSizeOverride("font_size", 12); // smaller font
dateLabel.AddThemeFontSizeOverride("font_size", 8); // smaller font




	// Add button to container
	container.AddChild(btn);

	// Load icon asynchronously
	LoadIconFromUrl(icon, iconsAndAchData[i].Key, unlocked);
}



	}

	private void GoBack()
	{
		var tree = GetTree();
		var returnScene = tree.HasMeta("pgemu_return_scene") ? tree.GetMeta("pgemu_return_scene").AsString() : null;
		returnScene = string.IsNullOrWhiteSpace(returnScene) ? "res://HomeScreen.tscn" : returnScene;

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


}
