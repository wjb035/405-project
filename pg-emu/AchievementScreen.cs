using Godot;
using System;
using PGEmu.app;
using System.Collections.Generic;


public partial class AchievementScreen : Control
{
	
	
	public override void _Ready()
	{
		
		Button back = GetNode<Button>("Bg/Margin/Root/TopBar/BtnBack");
		back.Pressed += GoBack;
		
		// Get the container
		VBoxContainer container = GetNode<VBoxContainer>("ScrollContainer/ButtonContainer");
		RetroAchievementsService.achievementGet(2689);
		
		
		var icons = new List<String>();
		if (AchievementStorage.achievementData != null){
			foreach (var g in AchievementStorage.achievementData)
			{
				//GD.Print(g.Value.Title + " has an id of " + g.Key + " with a badge url of " + g.Value.BadgeName + " unlocked on " + g.Value.EarnedDate);
				icons.Add("https://media.retroachievements.org/Badge/"+g.Value.BadgeName+".png");
			}
		}
		for (int i = 0; i < AchievementStorage.achievementData.Count; i++){
			GD.Print(AchievementStorage.achievementData[i].Value.Title);
		}
		

		for (int i = 0; i < icons.Count; i++)
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
titleLabel.Text = AchievementStorage.achievementData[i].Value.Title;
titleLabel.SizeFlagsHorizontal = SizeFlags.Fill;
titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
textBox.AddChild(titleLabel);

// Second Label (Subtitle / Description)
Label subtitleLabel = new Label();
subtitleLabel.Text = AchievementStorage.achievementData[i].Value.Title; // or whatever field you use
subtitleLabel.SizeFlagsHorizontal = SizeFlags.Fill;
subtitleLabel.HorizontalAlignment = HorizontalAlignment.Left;

// Optional: make it visually secondary
//subtitleLabel.Modulate = new Color(0.8f, 0.8f, 0.8f); // slightly dimmer
//subtitleLabel.AddThemeFontSizeOverride("font_size", 12); // smaller font

textBox.AddChild(subtitleLabel);


	// Add button to container
	container.AddChild(btn);

	// Load icon asynchronously
	LoadIconFromUrl(icon, icons[i]);
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
	private void LoadIconFromUrl(TextureRect textureRect, string url)
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

		
		ImageTexture tex = ImageTexture.CreateFromImage(img);
		textureRect.Texture = tex;

		
		request.QueueFree();
	};

	
	var errRequest = request.Request(url);
	if (errRequest != Error.Ok)
		GD.PrintErr($"Failed to start HTTP request: {url}");
}


}
