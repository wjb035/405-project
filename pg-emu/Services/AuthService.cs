using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using HttpClient = Godot.HttpClient;

namespace PGEmu.Services;

public partial class AuthService : Node
{
	public static AuthService Instance { get; private set; }
	
	private const string BackendOrigin = "http://localhost:5276";
	private static string BaseUrl => $"{BackendOrigin}/api/auth/";
	private const int BackendStartupTimeoutMs = 10000;
	private const string SavePath = "user://auth.json";
	private static readonly object BackendStartLock = new();
	private static readonly System.Net.Http.HttpClient BackendProbeClient = new()
	{
		Timeout = TimeSpan.FromSeconds(1.5)
	};
	private static Process? _backendProcess;
	private static Task<bool>? _backendEnsureTask;
	private static string? _backendLaunchFailure;

	public string AccessToken { get; private set; } = "";
	public string RefreshToken { get; private set; } = "";
	public string Username { get; private set; } = "";
	public string LastErrorMessage { get; private set; } = "";
	public Guid UserId { get; private set; }
	
	public void SetTokens(string accessToken, string refreshToken, string username)
	{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		Username = username;
	}
	
	
	
	public Guid getUID(){
		LoadTokensFromDisk();
		
		var handler = new JwtSecurityTokenHandler();
		var token = handler.ReadJwtToken(AccessToken);
		var userIdClaim = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

		if (!string.IsNullOrEmpty(userIdClaim))
		{
			UserId = Guid.Parse(userIdClaim);
		}
		GD.Print(UserId);
		return UserId;
	}

	public override void _Ready()
	{
		Instance = this;
		LoadTokensFromDisk();
		_ = EnsureBackendAvailableAsync();
		ParseUserIdFromToken();
		if (!string.IsNullOrEmpty(AccessToken))
		{
			var handler = new JwtSecurityTokenHandler();
			var token = handler.ReadJwtToken(AccessToken);
			var userIdClaim = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
			if (!string.IsNullOrEmpty(userIdClaim))
				UserId = Guid.Parse(userIdClaim);
		}
		else
		{
			ParseUserIdFromToken(); 
		}
	}
	
	// LOGIN
	public async Task<bool> Login(string username, string password)
	{
		var response = await SendRequest(
			BaseUrl + "login",
			new { username, password }
		);

		if (response == null)
			return false;

		AccessToken = response.Value.GetProperty("accessToken").GetString()!;
		RefreshToken = response.Value.GetProperty("refreshToken").GetString()!;
		Username = response.Value.GetProperty("username").GetString()!;
		//UserId = Guid.Parse(response.Value.GetProperty("userId").GetString()!);
		SetTokens(AccessToken, RefreshToken, Username);

		SaveTokensToDisk();
		return true;
	}
	
	// REGISTER
	public async Task<bool> Register(string username, string email, string password)
	{
		var response = await SendRequest(
			BaseUrl + "register",
			new { username, email, password }
		);

		if (response == null)
			return false;
		
		AccessToken = response.Value.GetProperty("accessToken").GetString()!;
		RefreshToken = response.Value.GetProperty("refreshToken").GetString()!;
		Username = response.Value.GetProperty("username").GetString()!;
		//UserId = Guid.Parse(response.Value.GetProperty("userId").GetString()!);
		SetTokens(AccessToken, RefreshToken, Username);

		SaveTokensToDisk();
		return true;
	}

	// FORGOT PASSWORD
	public async Task<bool> ForgotPassword(string email)
	{
		var response = await SendRequest(
			BaseUrl + "forgot-password",
			new { email }
		);
		return response != null;
	}
	
	// RESET PASSWORD
	public async Task<(bool success, string message)> ResetPassword(string email, string code, string newPassword)
	{
		var response = await SendRequest(
			BaseUrl + "reset-password",
			new { email, code, newPassword }
		);
		if (response == null)
			return (false, "Invalid reset password");
		
		var msg = response.Value.TryGetProperty("message", out var m) ? m.GetString()! : "";
		return  (true, msg);
	}
	
	// REFRESH
	public async Task<bool> Refresh()
	{
		if (string.IsNullOrEmpty(RefreshToken))
			return false;

		var response = await SendRequest(
			BaseUrl + "refresh",
			new { refreshToken = RefreshToken }
		);

		if (response == null)
			return false;

		AccessToken = response.Value.GetProperty("accessToken").GetString()!;
		RefreshToken = response.Value.GetProperty("refreshToken").GetString()!;

		SaveTokensToDisk();
		return true;
	}
	
	// AUTHORIZED REQUEST WITH AUTO RETRY
	public event Action? SessionExpired;
	public async Task<JsonElement?> SendAuthorizedRequest(string endpoint, 
		object? body = null, 
		HttpMethod? method = null)
	{
		method ??= body != null ? HttpMethod.Post : HttpMethod.Get;
		
		var response = await SendRequest(endpoint, body, true, method);

		if (response == null)
		{
			// Possibly expired — try refresh
			var refreshed = await Refresh();

			if (!refreshed)
			{
				SessionExpired?.Invoke();
				return null;
			}

			// Retry once
			response = await SendRequest(endpoint, body, true, method);
		}

		return response;
	}
	
	// CORE HTTP METHOD
	private async Task<JsonElement?> SendRequest(
		string url,
		object? body,
		bool authorized = false,
		HttpMethod? method = null)
	{
		LastErrorMessage = "";
		method ??= body != null ? HttpMethod.Post : HttpMethod.Get;

		if (!await EnsureBackendAvailableAsync())
		{
			LastErrorMessage = string.IsNullOrWhiteSpace(_backendLaunchFailure)
				? $"Could not reach backend at {BaseUrl.TrimEnd('/')}. Start PGEmu.backend and make sure MySQL is available."
				: $"Backend failed to start: {_backendLaunchFailure}";
			return null;
		}
		
		// convert HttpMethod to Godot's enum
		var godotMethod = method == HttpMethod.Put    ? HttpClient.Method.Put
			: method == HttpMethod.Delete ? HttpClient.Method.Delete
			: method == HttpMethod.Post   ? HttpClient.Method.Post
			:                               HttpClient.Method.Get;

		
		var http = new HttpRequest();
		AddChild(http);

		var headers = new System.Collections.Generic.List<string>
		{
            "Content-Type: application/json"
		};

		if (authorized && !string.IsNullOrEmpty(AccessToken))
			headers.Add("Authorization: Bearer " + AccessToken);

		string json = body != null
			? JsonSerializer.Serialize(body)
			: "";

		var tcs = new TaskCompletionSource<JsonElement?>();

		http.RequestCompleted += (result, code, h, b) =>
		{
			if (result != (long)HttpRequest.Result.Success)
			{
				LastErrorMessage = string.IsNullOrWhiteSpace(_backendLaunchFailure)
					? $"Could not reach backend at {BaseUrl.TrimEnd('/')}."
					: $"Could not reach backend at {BaseUrl.TrimEnd('/')}. {_backendLaunchFailure}";
				GD.Print($"Request transport failed: result={result} url={url}");
				tcs.SetResult(null);
			}
			else if (code >= 200 && code < 300)
			{
				var text = Encoding.UTF8.GetString(b);
				var doc = JsonDocument.Parse(text);
				tcs.SetResult(doc.RootElement);
			}
			else if (code == 401)
			{
				LastErrorMessage = "Invalid username or password.";
				tcs.SetResult(null);
			}
			else
			{
				var text = b.Length > 0 ? Encoding.UTF8.GetString(b) : "";
				LastErrorMessage = string.IsNullOrWhiteSpace(text)
					? $"Request failed ({code})."
					: text;
				GD.Print($"Request failed: {code} {url}");
				tcs.SetResult(null);
			}

			http.QueueFree();
		};

		var requestError = http.Request(
			url,
			headers.ToArray(),
			godotMethod, 
			json
		);

		if (requestError != Error.Ok)
		{
			LastErrorMessage = $"Could not start request to backend ({requestError}).";
			http.QueueFree();
			return null;
		}

		return await tcs.Task;
	}

	private static async Task<bool> EnsureBackendAvailableAsync()
	{
		if (await IsBackendReachableAsync())
			return true;

		Task<bool> pendingTask;
		lock (BackendStartLock)
		{
			if (_backendEnsureTask == null || _backendEnsureTask.IsCompleted)
				_backendEnsureTask = EnsureBackendAvailableCoreAsync();
			pendingTask = _backendEnsureTask;
		}

		return await pendingTask;
	}

	private static async Task<bool> EnsureBackendAvailableCoreAsync()
	{
		if (await IsBackendReachableAsync())
			return true;

		TryStartBackendProcess();

		var stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < BackendStartupTimeoutMs)
		{
			if (await IsBackendReachableAsync())
				return true;

			if (_backendProcess != null && _backendProcess.HasExited)
				break;

			await Task.Delay(250);
		}

		return await IsBackendReachableAsync();
	}

	private static async Task<bool> IsBackendReachableAsync()
	{
		try
		{
			using var response = await BackendProbeClient.GetAsync($"{BackendOrigin}/swagger/index.html");
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void TryStartBackendProcess()
	{
		if (_backendProcess != null && !_backendProcess.HasExited)
			return;

		if (!TryCreateBackendStartInfo(out var startInfo))
			return;

		try
		{
			var process = Process.Start(startInfo);
			if (process == null)
			{
				_backendLaunchFailure = "Process start returned no handle.";
				return;
			}

			process.OutputDataReceived += (_, args) =>
			{
				if (!string.IsNullOrWhiteSpace(args.Data))
					GD.Print($"[backend] {args.Data}");
			};
			process.ErrorDataReceived += (_, args) =>
			{
				if (!string.IsNullOrWhiteSpace(args.Data))
				{
					_backendLaunchFailure = args.Data;
					GD.PushWarning($"[backend] {args.Data}");
				}
			};
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			_backendLaunchFailure = null;
			_backendProcess = process;
		}
		catch (Exception ex)
		{
			_backendLaunchFailure = ex.Message;
		}
	}

	private static bool TryCreateBackendStartInfo(out ProcessStartInfo startInfo)
	{
		var backendProjectDir = ResolveBackendProjectDir();
		var backendBinDir = Path.Combine(backendProjectDir, "bin", "Debug", "net10.0");
		var backendDllPath = Path.Combine(backendBinDir, "PGEmu.backend.dll");

		if (File.Exists(backendDllPath))
		{
			startInfo = CreateBaseStartInfo(backendBinDir);
			startInfo.ArgumentList.Add(backendDllPath);
			return true;
		}

		var backendProjectPath = Path.Combine(backendProjectDir, "PGEmu.backend.csproj");
		if (File.Exists(backendProjectPath))
		{
			startInfo = CreateBaseStartInfo(backendProjectDir);
			startInfo.ArgumentList.Add("run");
			startInfo.ArgumentList.Add("--project");
			startInfo.ArgumentList.Add(backendProjectPath);
			startInfo.ArgumentList.Add("--no-launch-profile");
			return true;
		}

		startInfo = new ProcessStartInfo();
		_backendLaunchFailure = $"Could not find PGEmu.backend under {backendProjectDir}.";
		return false;
	}

	private static ProcessStartInfo CreateBaseStartInfo(string workingDirectory)
	{
		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
		startInfo.Environment["ASPNETCORE_URLS"] = BackendOrigin;
		return startInfo;
	}

	private static string ResolveBackendProjectDir()
	{
		var projectDir = ProjectSettings.GlobalizePath("res://");
		return Path.GetFullPath(Path.Combine(projectDir, "..", "PGEmu.backend"));
	}
	
	// PERSISTENCE
	private void SaveTokensToDisk()
	{
		var data = new
		{
			AccessToken,
			RefreshToken,
			Username
		};

		var json = JsonSerializer.Serialize(data);
		using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
		file.StoreString(json);    
	}
	private void LoadTokensFromDisk()
	{
		if (!Godot.FileAccess.FileExists(SavePath))
			return;

		var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
		var text = file.GetAsText();

		var doc = JsonDocument.Parse(text);
		
		AccessToken = doc.RootElement.TryGetProperty("AccessToken", out var a) ? a.GetString() ?? "" : "";
		RefreshToken = doc.RootElement.TryGetProperty("RefreshToken", out var r) ? r.GetString() ?? "" : "";
		Username = doc.RootElement.TryGetProperty("Username", out var u) ? u.GetString() ?? "" : "";
	}

	// what do you think this does
	public async Task Logout()
	{
		if (!string.IsNullOrEmpty(RefreshToken))
			await SendRequest(BaseUrl + "logout", new { refreshToken = RefreshToken });
		
		AccessToken = "";
		RefreshToken = "";
		if (Godot.FileAccess.FileExists(SavePath))
		{
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
		}
	}
	
	public bool IsLoggedIn()
	{
		if (string.IsNullOrEmpty(AccessToken)) return false;
    
		try
		{
			var handler = new JwtSecurityTokenHandler();
			var token = handler.ReadJwtToken(AccessToken);
			return token.ValidTo > DateTime.UtcNow;
		}
		catch
		{
			return false;
		}
	}
	
	// DOES WHAT IT SAY
	private void ParseUserIdFromToken()
	{
		if (string.IsNullOrEmpty(AccessToken)) return;
		var handler = new JwtSecurityTokenHandler();
		var token = handler.ReadJwtToken(AccessToken);
		var sub = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
		if (!string.IsNullOrEmpty(sub))
			UserId = Guid.Parse(sub);
	}
}
