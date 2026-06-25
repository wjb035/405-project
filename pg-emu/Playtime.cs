using Godot;
using System;
using System.Diagnostics;
using System.IO;
using PGEmu.app;
using PGEmu.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


public partial class Playtime : Node
{
	private Timer checkTimer;
	private Process runningGame;
	private int totalSecondsPlayed = 0;
	private GameEntry currentRunningGame = null;
	private string currentPlatform;
	
	
	public override void _Ready()
	{
		PlaytimeStorage.LoadFromJson();
		checkTimer = new Timer();
		checkTimer.WaitTime = 10;
		checkTimer.Autostart = false;
		checkTimer.OneShot = false;

		AddChild(checkTimer);
		checkTimer.Timeout += OnTimerTimeout;
	}


	public void FindPlatform(PlatformConfig? platformCheck, GameEntry? currentGame, string? emulatorExePath = null)
	{
		if (currentGame == null || platformCheck == null)
			return;

		currentGame.LastPlayedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		AddOrUpdateTrackedGame(platformCheck, currentGame);
		PlaytimeStorage.SaveToJson();

		KillOtherEmulatorProcesses(platformCheck.DefaultEmulatorId);

		var candidates = ResolveProcessNameCandidates(platformCheck.DefaultEmulatorId, emulatorExePath);
		if (candidates.Count == 0)
		{
			GD.PrintErr(
				$"Playtime: no process-name candidates for emulator '{platformCheck.DefaultEmulatorId}'. " +
				"Configure the emulator's exePath or add an entry to PlaytimeStorage.EmulatorToName.");
			return;
		}

		GD.Print(
			$"Playtime: looking for emulator process for '{platformCheck.DefaultEmulatorId}'. " +
			$"Candidates: [{string.Join(", ", candidates)}]");
		_ = AttachAndMonitorAsync(platformCheck, currentGame, candidates);
	}

	private static void KillOtherEmulatorProcesses(string? currentEmulatorId)
	{
		// Kill any other recognized emulator we know how to name. This keeps the
		// behavior of "only one emulator runs at a time" without using a hardcoded
		// list inside FindPlatform.
		foreach (var kvp in PlaytimeStorage.EmulatorToName)
		{
			if (kvp.Key == currentEmulatorId)
				continue;
			try
			{
				foreach (var p in Process.GetProcessesByName(kvp.Value))
				{
					try { p.Kill(); } catch { /* ignore */ }
				}
			}
			catch { /* ignore */ }
		}
	}

	private static List<string> ResolveProcessNameCandidates(string? emulatorId, string? exePath)
	{
		// Build an ordered, de-duplicated list of plausible process names. We try
		// the hardcoded EmulatorToName entry first (legacy behavior) and then derive
		// extra candidates from the configured exePath so we still match on macOS,
		// where the actual binary name (e.g. "PCSX2") rarely matches the Windows /
		// Linux name (e.g. "PCSX2-v2.6.3" or "pcsx2-qt").
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var ordered = new List<string>();

		void Add(string? name)
		{
			if (string.IsNullOrWhiteSpace(name)) return;
			if (seen.Add(name)) ordered.Add(name);
		}

		if (!string.IsNullOrEmpty(emulatorId)
			&& PlaytimeStorage.EmulatorToName.TryGetValue(emulatorId, out var primary))
		{
			Add(primary);
		}

		if (!string.IsNullOrWhiteSpace(exePath))
		{
			var trimmed = exePath.TrimEnd('/', '\\');
			var fileName = Path.GetFileName(trimmed);
			var stripped = StripKnownExecutableExtension(fileName);
			Add(stripped);

			// Strip a trailing version suffix like "PCSX2-v2.6.3" -> "PCSX2".
			var dashIdx = stripped.IndexOf('-');
			if (dashIdx > 0)
				Add(stripped.Substring(0, dashIdx));

			// On macOS, the binary inside an .app bundle is whatever the Info.plist
			// declares as CFBundleExecutable, which is what shows up in `ps`.
			if (OperatingSystem.IsMacOS()
				&& trimmed.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
			{
				Add(TryReadAppBundleExecutable(trimmed));
			}
		}

		return ordered;
	}

	private static string StripKnownExecutableExtension(string fileName)
	{
		foreach (var ext in new[] { ".app", ".exe" })
		{
			if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
				return fileName.Substring(0, fileName.Length - ext.Length);
		}
		return fileName;
	}

	private static string? TryReadAppBundleExecutable(string appBundlePath)
	{
		try
		{
			var infoPath = Path.Combine(appBundlePath, "Contents", "Info.plist");
			if (!File.Exists(infoPath)) return null;

			var content = File.ReadAllText(infoPath);
			var keyIdx = content.IndexOf("<key>CFBundleExecutable</key>", StringComparison.Ordinal);
			if (keyIdx < 0) return null;

			var startIdx = content.IndexOf("<string>", keyIdx, StringComparison.Ordinal);
			if (startIdx < 0) return null;
			startIdx += "<string>".Length;

			var endIdx = content.IndexOf("</string>", startIdx, StringComparison.Ordinal);
			if (endIdx < 0) return null;

			return content.Substring(startIdx, endIdx - startIdx).Trim();
		}
		catch
		{
			return null;
		}
	}

	private async Task AttachAndMonitorAsync(
		PlatformConfig platform,
		GameEntry currentGame,
		List<string> candidateNames)
	{
		// Process detection has to poll because on macOS .app bundles are launched
		// via `open -a`, which spawns the real emulator asynchronously. The actual
		// process may not exist yet by the time we get here.
		Process? found = null;
		string? matchedName = null;
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

		while (DateTime.UtcNow < deadline)
		{
			foreach (var name in candidateNames)
			{
				Process[] procs;
				try { procs = Process.GetProcessesByName(name); }
				catch { continue; }

				if (procs.Length == 0) continue;

				var sorted = procs
					.OrderByDescending(p =>
					{
						try { return p.StartTime; }
						catch { return DateTime.MinValue; }
					})
					.ToArray();

				// Newest process is the one we just launched; kill the rest so we
				// don't have stray emulator instances running.
				for (int i = 1; i < sorted.Length; i++)
				{
					try { sorted[i].Kill(); } catch { /* ignore */ }
				}

				found = sorted[0];
				matchedName = name;
				break;
			}

			if (found != null) break;
			await Task.Delay(300);
		}

		if (found == null)
		{
			GD.PrintErr(
				$"Playtime: emulator process not detected within timeout for '{platform.DefaultEmulatorId}'. " +
				$"Tried: [{string.Join(", ", candidateNames)}]. Playtime will not be tracked for this session.");
			InputRoutingService.Instance?.UnlockUiInput();
			return;
		}

		GD.Print(
			$"Playtime: tracking '{currentGame.Name}' via process '{matchedName}' (PID {found.Id}).");
		StartMonitoring(found);
	}

	private void AddOrUpdateTrackedGame(PlatformConfig platform, GameEntry currentGame)
	{
		currentPlatform = platform.DefaultEmulatorId;

		foreach (var p in PlaytimeStorage.playtimeTracked)
		{
			if (p.Key != platform.DefaultEmulatorId) continue;

			foreach (var g in p.Value)
			{
				if (g.Name != currentGame.Name) continue;
				currentRunningGame = g;
				currentRunningGame.LastPlayedUnixTime = currentGame.LastPlayedUnixTime;
				GD.Print($"Playtime: '{g.Name}' already tracked, total {currentRunningGame.TimePlayed}s.");
				return;
			}

			p.Value.Add(currentGame);
			currentRunningGame = currentGame;
			GD.Print($"Playtime: added new entry for '{currentGame.Name}' under '{platform.DefaultEmulatorId}'.");
			return;
		}

		PlaytimeStorage.playtimeTracked.Add(new KeyValuePair<string, List<GameEntry>>(
			platform.DefaultEmulatorId ?? string.Empty,
			new List<GameEntry> { currentGame }));
		currentRunningGame = currentGame;
		GD.Print($"Playtime: registered new emulator '{platform.DefaultEmulatorId}' with first game '{currentGame.Name}'.");
	}
	public void StartMonitoring(Process gameProcess)
	{
		runningGame = gameProcess;
		checkTimer.Start();
	}

	private void OnTimerTimeout()
	{
		
		if (runningGame == null)
			return;

		if (runningGame.HasExited)
		{
			GD.Print("Game closed!");
			checkTimer.Stop();
			InputRoutingService.Instance?.UnlockUiInput();
			OnGameClosed();
			PlaytimeStorage.SaveToJson();
			GD.Print("Playtime Saved!");
		}
		else
		{
 			currentRunningGame.TimePlayed+=10;
			GD.Print(currentRunningGame.Name + " has been running for " + currentRunningGame.TimePlayed + " seconds");
			GD.Print("Game still running...");
			PlaytimeStorage.SaveToJson();
		}
	}

	public async void OnGameClosed()
{
	GD.Print("Game closed! Sending playtime...");
	
	//Set the status as being in the menus, since the user isn't playing anymore
	ActivityManager.SetActivity("Playing", "In the Menus", "GodotClient");
	// Get JWT token from your autoload
	var authService = (PGEmu.Services.AuthService)GetNode("/root/AuthService");
	await authService.Refresh(); // refresh token first

	string jwtToken = authService.AccessToken;

	GD.Print("JWT token: " + jwtToken);
	if (currentRunningGame != null)
	{
		 SendPlaytimeToServer(
			jwtToken,
			currentRunningGame.Name,     // your ExternalGameId
			currentRunningGame.TimePlayed,
			currentPlatform ?? "unknown"
		);
	}
}
	
	private async Task SendPlaytimeToServer(string jwtToken, string externalGameId, int secondsPlayed, string platformId)
{
	var http = new HttpRequest();
	AddChild(http); // Must be added to scene tree

	string url = "http://localhost:5276/api/usergames/playtime";

	var headers = new string[]
	{
		"Content-Type: application/json",
		"Authorization: Bearer " + jwtToken
	};

	var body = new
{
	ExternalGameId = externalGameId,
	Source = 0,
	SecondsPlayed = secondsPlayed,
	PlatformId = platformId
};

	string json = System.Text.Json.JsonSerializer.Serialize(body);

	var tcs = new TaskCompletionSource<bool>();

	http.RequestCompleted += (result, responseCode, responseHeaders, bodyBytes) =>
	{
		if (responseCode == 200)
			GD.Print("Playtime successfully sent!");
		else
			GD.Print("Failed to send playtime: ", responseCode);

		tcs.SetResult(true);
		http.QueueFree(); // cleanup
	};

	http.Request(url, headers, HttpClient.Method.Put, json);

	await tcs.Task;
}
}
