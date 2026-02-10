using Godot;
using System;


public partial class AchievementScreen : Control
{
	
	
	public override void _Ready()
	{
		
		Button back = GetNode<Button>("Bg/Margin/Root/TopBar/BtnBack");
		back.Pressed += GoBack;
		
		// Get the container
		VBoxContainer container = GetNode<VBoxContainer>("ScrollContainer/ButtonContainer");

		// Example URLs (replace with real URLs)
		string[] iconUrls = {
			"https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTD21JqcIbkZomTjisCvLrwPCbTZQKMFeCL-Q&s",
			"https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTD21JqcIbkZomTjisCvLrwPCbTZQKMFeCL-Q&s",
			"https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTD21JqcIbkZomTjisCvLrwPCbTZQKMFeCL-Q&s",
			"https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTD21JqcIbkZomTjisCvLrwPCbTZQKMFeCL-Q&s",
			"https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTD21JqcIbkZomTjisCvLrwPCbTZQKMFeCL-Q&s",
		};

		for (int i = 0; i < iconUrls.Length; i++)
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

	// Label
	Label label = new Label();
	label.Text = $"Achievement {i + 1}";
	label.SizeFlagsHorizontal = SizeFlags.Fill;
	label.SizeFlagsVertical = SizeFlags.Fill;
	label.HorizontalAlignment = HorizontalAlignment.Left;
	hbox.AddChild(label);

	// Add button to container
	container.AddChild(btn);

	// Load icon asynchronously
	LoadIconFromUrl(icon, iconUrls[i]);
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
		Error err = img.LoadJpgFromBuffer(body); 
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
