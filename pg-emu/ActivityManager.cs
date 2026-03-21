using Godot;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using PGEmu.Services;

public static class ActivityManager
{
	private const string BaseUrl = "http://localhost:5276/api/activity/set";

	public static async Task SetActivity(string activityType, string externalGameId = "", string source = "GodotClient")
	{
		GD.Print("HI HI FROM THE ACTMANAGER");

		var authService = AuthService.Instance;
		if (authService == null || string.IsNullOrEmpty(authService.AccessToken))
		{
			GD.PrintErr("[ActivityManager] No auth token, cannot update activity.");
			return;
		}

		var payload = new
		{
			UserId = authService.getUID(),
			ActivityType = 0,
			ExternalGameId = externalGameId,
			Source = 1
		};

		string json = JsonSerializer.Serialize(payload);

		var http = new HttpRequest();
		var root = Engine.GetMainLoop() as SceneTree; // get root to add HttpRequest
		root.Root.AddChild(http);

		string[] headers = new string[]
		{
			"Content-Type: application/json",
			"Authorization: Bearer " + authService.AccessToken
		};

		var tcs = new TaskCompletionSource<bool>();

		http.RequestCompleted += (result, code, responseHeaders, body) =>
		{
			GD.Print($"[ActivityManager] RequestCompleted fired. Code={code}");
			if (code == 200)
				GD.Print("[ActivityManager] Activity set successfully.");
			else
				GD.PrintErr($"[ActivityManager] Failed to set activity. HTTP {code}");

			http.QueueFree();
			tcs.SetResult(code == 200);
		};

		GD.Print("[ActivityManager] Sending activity request: " + json);
		http.Request(BaseUrl, headers, HttpClient.Method.Post, json);

		await tcs.Task;
	}
}
