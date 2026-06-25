using Godot;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using PGEmu.Services;



public static class ActivityManager
{
	private const string BaseUrl = "http://localhost:5276/api/activity/set";
	private static Timer checkTimer;
	
	private static bool timerRunning = false;
	
	static string lastAct = null;
	
	public static void runTimer(){
		if (!timerRunning){
			checkTimer = new Timer();
			checkTimer.WaitTime = 60;
			checkTimer.Autostart = true;
			checkTimer.OneShot = false;

			var root = Engine.GetMainLoop() as SceneTree;
			if (root?.Root == null)
			{
				GD.PrintErr("[ActivityManager] Scene root not available, cannot start activity timer.");
				return;
			}

			// HomeScreen calls this during _Ready; defer add to avoid "parent busy setting up children".
			root.Root.CallDeferred(Node.MethodName.AddChild, checkTimer);

			checkTimer.Timeout += OnTimerTimeout;
			timerRunning = true;
		}
	}
	
	
	
	
	private static void OnTimerTimeout(){
		if (lastAct != null){
			GD.Print("Updating the status!");
			SetActivity("", lastAct, "");
		}
	}
	public static async Task SetActivity(string activityType, string externalGameId = "", string source = "GodotClient")
	{
		GD.Print("HI HI FROM THE ACTMANAGER");
		
		var authService = AuthService.Instance;
		if (authService == null || string.IsNullOrEmpty(authService.AccessToken))
		{
			GD.PrintErr("[ActivityManager] No auth token, cannot update activity.");
			return;
		}
		lastAct = externalGameId;
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
		if (root?.Root == null)
		{
			GD.PrintErr("[ActivityManager] Scene root not available, cannot create HTTP request.");
			return;
		}

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
		// Defer add/request so we don't call Request before the node is in-tree.
		root.Root.CallDeferred(Node.MethodName.AddChild, http);
		Callable.From(() =>
		{
			if (!http.IsInsideTree())
			{
				GD.PrintErr("[ActivityManager] HttpRequest is not inside tree.");
				http.QueueFree();
				tcs.TrySetResult(false);
				return;
			}

			var requestError = http.Request(BaseUrl, headers, HttpClient.Method.Post, json);
			if (requestError != Error.Ok)
			{
				GD.PrintErr($"[ActivityManager] Request failed to start: {requestError}");
				http.QueueFree();
				tcs.TrySetResult(false);
			}
		}).CallDeferred();

		await tcs.Task;
	}
}
