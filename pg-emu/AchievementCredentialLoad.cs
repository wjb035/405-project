using Godot;
using System;
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

public partial class AchievementCredentialLoad : Node
{
	
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};
	// Called when the node enters the scene tree for the first time.
	public async override void _Ready()
	{
		await LoadFromJson();
		GD.Print("AUTOLOADED THE AWARD MANAGER!!!");
		RetroAchievementsService.awardedGames();
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
		
		
		GD.Print("credentials autoloaded!");
		
		GD.Print(fileContents.Key);
	
		GD.Print(fileContents.Value);
		
		
		GD.Print(RetroAchievementsService.username);
		GD.Print(RetroAchievementsService.apiKey);
		
		if (RetroAchievementsService.username == fileContents.Key){
			GD.Print("usernames are the same");
		}
		
		if (RetroAchievementsService.apiKey == fileContents.Value){
			GD.Print("api keys are the same");
		}
		RetroAchievementsService.username = fileContents.Key;
		RetroAchievementsService.apiKey = fileContents.Value;
		RetroAchievementsService.client = new RetroAchievementsHttpClient(new RetroAchievementsAuthenticationData(fileContents.Key, fileContents.Value));
		
	}
	
	
}
