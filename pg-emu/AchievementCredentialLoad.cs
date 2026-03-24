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


public partial class AchievementCredentialLoad : Node
{
	
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		LoadFromJson();
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
		
		RetroAchievementsService.username = fileContents.Key;
		RetroAchievementsService.apiKey = fileContents.Value;
	
	}
	
	
}
