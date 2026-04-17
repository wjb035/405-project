using Godot;
using System;
using PGEmu.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using PGEmu.app;
using RetroAchievements.Api;
using Godot;
using RetroAchievements.Api.Response.Users.Records;

public partial class AchievementSettings : Control
{
	[Export] NodePath UsernameEditPath;
	
	[Export] NodePath ApiEditPath;
	[Export] NodePath ApiSaveButtonPath;
	
	private LineEdit _usernameEdit;
	private Button? _usernameSave;
	private LineEdit _apiEdit;
	private Button _apiSave;
	private LineEdit _avatarEdit;
	private Button _avatarSave;
	private Label _status = null!;
	ProfileService _profileService = new ProfileService();

	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_usernameEdit = GetNode<LineEdit>(UsernameEditPath);
		
		_apiEdit = GetNode<LineEdit>(ApiEditPath);
		_apiSave = GetNode<Button>(ApiSaveButtonPath);
		
		_status = GetNode<Label>("Margin/Root/Status");

		ApplyThemeAesthetic();
	
		
		_apiSave.Pressed += ChangeApi;
		//LoadFromJson();

	}
	
	public static async Task LoadFromJson(){
		if (!File.Exists("credentials.json")){
			GD.Print("Oops! No credentials.json file was found!");
			return;
		}
		
		await using FileStream openStream = File.OpenRead("credentials.json");
		var fileContents = await JsonSerializer.DeserializeAsync<
			KeyValuePair<string,string>
		>(openStream, _jsonOptions);
		
		GD.Print(fileContents.Key);
	
	}
	
	public void ChangeApi() 
	{
		GD.Print("text pressed in achievement settings!");
		if (_usernameEdit.Text?.Trim() =="" && _apiEdit.Text?.Trim() != ""){
			_status.Text = "Username can't be empty!";
		}
		else if (_apiEdit.Text?.Trim() == "" && _usernameEdit.Text?.Trim() != ""){
			_status.Text = "API Key can't be empty!";
		}
		else if (_apiEdit.Text?.Trim() == "" && _usernameEdit.Text?.Trim() ==""){
			_status.Text = "Username and API Key can't be empty!";
		}
		else{
			GD.Print(_usernameEdit.Text?.Trim());
			GD.Print(_apiEdit.Text?.Trim());
			
			RetroAchievementsService.username = _usernameEdit.Text?.Trim();
			RetroAchievementsService.apiKey = _apiEdit.Text?.Trim();
			RetroAchievementsService.client = new RetroAchievementsHttpClient(new RetroAchievementsAuthenticationData(RetroAchievementsService.username, RetroAchievementsService.apiKey));
		
			GD.Print(RetroAchievementsService.apiKey);
			//vwhMq54xiImAMo35mdQ20UGgYtktA4pK
			
			SaveToJson(new KeyValuePair<string,string>(_usernameEdit.Text?.Trim(),_apiEdit.Text?.Trim()));
			_status.Text = "RetroAcheivements Settings updated.";
			
		}
	}
	
	
	public static async Task SaveToJson(KeyValuePair<string, string> credentials){
		await using FileStream createStream = File.Create("credentials.json");
		await JsonSerializer.SerializeAsync(createStream, credentials, _jsonOptions);
		Console.WriteLine("Credential data saved.");
	}
	
	
	private void ApplyThemeAesthetic()
	{
		var bg = GetNodeOrNull<ColorRect>("Bg");
		if (bg != null)
			bg.Color = new Color(0.068f, 0.048f, 0.121f, 1f);

		var title = GetNodeOrNull<Label>("Margin/Root/Title");
		UiStyle.StyleTitleLabel(title);

		var usernameLabel = GetNodeOrNull<Label>("Margin/Root/ChangeUsernameLabel");
		var apiLabel = GetNodeOrNull<Label>("Margin/Root/ChangeApiLabel3");
		
		UiStyle.StyleMetaLabel(usernameLabel);
		UiStyle.StyleMetaLabel(apiLabel);
		

		UiStyle.StyleLineEdit(_usernameEdit);
		UiStyle.ApplyParallaxShadow(_usernameEdit);
		UiStyle.StyleLineEdit(_apiEdit);
		UiStyle.ApplyParallaxShadow(_apiEdit);
		
		_usernameEdit.PlaceholderText = "RetroAchievements username";
		_apiEdit.PlaceholderText = "RetroAchievements API Key";
		

		UiStyle.StylePrimaryButton(_apiSave);
		UiStyle.AddHoverFeedback(_apiSave);
		UiStyle.ApplyParallaxShadow(_apiSave);
		
		UiStyle.TightenButtonContentPadding(_apiSave, horizontal: 6f, vertical: 2f);
		

		UiStyle.StyleStatusLabel(_status);
		_status.Text = "";
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
