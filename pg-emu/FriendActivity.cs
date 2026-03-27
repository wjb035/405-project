using Godot;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PGEmu.Services;
using System.Collections.Generic;

public static class FriendActivity
{
	private const string BaseUrl = "http://localhost:5276/api/friends";
	private static List<KeyValuePair<string,string>> friendIds = new();
	public static Dictionary<KeyValuePair<string,string>, JsonElement> results = new Dictionary<KeyValuePair<string,string>, JsonElement>();

	public static async Task<Dictionary<KeyValuePair<string,string>, JsonElement>> GetActivitiesAsync()
{
	var authService = AuthService.Instance;
	await authService.Refresh();
	if (authService == null || string.IsNullOrEmpty(authService.AccessToken))
	{
		GD.PrintErr("[ActivityFetcher] No auth token.");
		return null;
	}

	var userIds = friendIds;
	var localResults = new Dictionary<KeyValuePair<string,string>, JsonElement>(); // use local
	
	
	foreach (var uid in userIds){
		localResults.Add(new KeyValuePair<string,string>(uid.Key, uid.Value), new JsonElement());
	}
	foreach (var userIdStr in userIds)
	{
		if (!Guid.TryParse(userIdStr.Value, out Guid userId))
		{
			GD.PrintErr($"Invalid GUID: {userIdStr}");
			continue;
		}

		var http = new HttpRequest();
		var root = Engine.GetMainLoop() as SceneTree;
		root.Root.AddChild(http);

		string[] headers = new string[]
		{
			"Authorization: Bearer " + authService.AccessToken
		};

		var taskCompletion = new TaskCompletionSource<JsonElement?>();

		http.RequestCompleted += (result, code, responseHeaders, body) =>
		{
			if (code == 200)
			{
				string json = Encoding.UTF8.GetString(body);
				try
				{
					var activity = JsonSerializer.Deserialize<JsonElement>(json);
					taskCompletion.SetResult(activity);
				}
				catch
				{
					taskCompletion.SetResult(null);
				}
			}
			else
			{
				GD.PrintErr($"Failed for user HTTP {code}");
				taskCompletion.SetResult(null);
			}

			http.QueueFree();
		};

		string activityUrl = $"http://localhost:5276/api/activity/{userId}";
		http.Request(activityUrl, headers, HttpClient.Method.Get);

		var activityResult = await taskCompletion.Task;
		if (activityResult.HasValue)
		{
			localResults[userIdStr] = activityResult.Value;
		}
	}

	results = localResults; 
	return results;
}




	public static async Task<string> GetFriendsJson()
	{
		var authService = AuthService.Instance;
		// THIS SEEMS LIKE I SHOULDN'T B E DOING THIS.... MAYBE FIX AFTER THE TOKEN ISSUE IS MERGED???
		await authService.Refresh();
		GD.Print("[FriendActivity] Using token: " + authService.AccessToken);
		
		
		if (authService == null || string.IsNullOrEmpty(authService.AccessToken))
		{
			GD.PrintErr("[FriendActivity] No auth token.");
			return null;
		}

		var http = new HttpRequest();
		var root = Engine.GetMainLoop() as SceneTree;
		root.Root.AddChild(http);

		string[] headers = new string[]
		{
			"Authorization: Bearer " + authService.AccessToken
		};

		var tcs = new TaskCompletionSource<string>();

		http.RequestCompleted += (result, code, responseHeaders, body) =>
		{
			GD.Print($"[FriendActivity] Response Code: {code}");

			if (code == 200)
			{
				string json = Encoding.UTF8.GetString(body);
				GD.Print("[FriendActivity] Friends data received.");
				tcs.SetResult(json);
				GD.Print(json);
				if (!string.IsNullOrEmpty(json))
					RegisterFriendsFromJson(json);
			}
			else
			{
				GD.PrintErr($"[FriendActivity] Failed: HTTP {code}");
				tcs.SetResult(null);
			}

			http.QueueFree();
		};

		GD.Print("[FriendActivity] Sending request...");
		http.Request(BaseUrl, headers, HttpClient.Method.Get);
		
		return await tcs.Task;
	}

	private static void RegisterFriendsFromJson(string json)
	{
		JsonElement friendsRoot;
		try
		{
			friendsRoot = JsonSerializer.Deserialize<JsonElement>(json);
		}
		catch (JsonException ex)
		{
			GD.PrintErr("[FriendActivity] Failed to parse friends JSON: " + ex.Message);
			return;
		}

		if (friendsRoot.ValueKind != JsonValueKind.Array)
			return;

		foreach (var friend in friendsRoot.EnumerateArray())
		{
			if (friend.ValueKind != JsonValueKind.Object)
				continue;

			var id = ReadJsonString(friend, "id");
			var username = ReadJsonString(friend, "username");
			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(username))
				continue;

			var pair = new KeyValuePair<string, string>(username, id);
			if (!friendIds.Contains(pair))
				friendIds.Add(pair);

			GD.Print("Friend ID: " + id);
			GD.Print("Friend Username: " + username);
		}
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
	
	
	
	
}
