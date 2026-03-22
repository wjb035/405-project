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
	private static List<String> friendIds = new();

	
	public static async Task<Dictionary<string, JsonElement>> GetActivitiesAsync(List<string> userIds)
{
	var authService = AuthService.Instance;
	await authService.Refresh();
	if (authService == null || string.IsNullOrEmpty(authService.AccessToken))
	{
		GD.PrintErr("[ActivityFetcher] No auth token.");
		return null;
	}

	var results = new Dictionary<string, JsonElement>();

	foreach (var userIdStr in userIds)
	{
		// ----------------------------
		// Convert string to Guid before calling the API
		// ----------------------------
		if (!Guid.TryParse(userIdStr, out Guid userId))
		{
			GD.PrintErr($"[ActivityFetcher] Invalid GUID: {userIdStr}, skipping...");
			continue; // skip invalid IDs
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
				GD.PrintErr($"[ActivityFetcher] Failed for user {userIdStr}, HTTP {code}");
				taskCompletion.SetResult(null);
			}

			http.QueueFree();
		};

		string activityUrl = $"http://localhost:5276/api/activity/{userId}";
	http.Request(activityUrl, headers, HttpClient.Method.Get);

		var activityResult = await taskCompletion.Task;
		if (activityResult.HasValue)
		{
			results[userIdStr] = activityResult.Value;

			// ----------------------------
			// Print the activity immediately
			// ----------------------------
			GD.Print($"User ID: {userIdStr}");
			foreach (var property in activityResult.Value.EnumerateObject())
			{
				GD.Print($"    {property.Name}: {property.Value}");
			}
		}
	}

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
{
	// Convert JSON array into a list of dictionary-like objects
	var friends = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);

	foreach (var friend in friends)
	{
		string id = friend["id"];
		string username = friend["username"];

		GD.Print("Friend ID: " + id);
		if (!friendIds.Contains(id)){
			friendIds.Add(id);
		}
		
		GD.Print("Friend Username: " + username);
	}
}		
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
		GetActivitiesAsync(friendIds);
		return await tcs.Task;
	}
	
	
	
	
}
