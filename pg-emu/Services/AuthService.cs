using Godot;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PGEmu.Services;

public partial class AuthService : Node
{
	public static AuthService Instance { get; private set; }
	
	private const string BaseUrl = "http://localhost:5276/api/auth/";
	private const string SavePath = "user://auth.json";

	public string AccessToken { get; private set; } = "";
	public string RefreshToken { get; private set; } = "";
	public string Username { get; private set; } = "";
	
	public void SetTokens(string accessToken, string refreshToken, string username)
	{
		AccessToken = accessToken;
		RefreshToken = refreshToken;
		Username = username;
	}

	public override void _Ready()
	{
		Instance = this;
		LoadTokensFromDisk();
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
		
		SetTokens(AccessToken, RefreshToken, Username);

		SaveTokensToDisk();
		return true;
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
	public async Task<JsonElement?> SendAuthorizedRequest(string endpoint)
	{
		var response = await SendRequest(endpoint, null, true);

		if (response == null)
		{
			// Possibly expired — try refresh
			var refreshed = await Refresh();

			if (!refreshed)
				return null;

			// Retry once
			response = await SendRequest(endpoint, null, true);
		}

		return response;
	}
	
	// CORE HTTP METHOD
	private async Task<JsonElement?> SendRequest(
		string url,
		object? body,
		bool authorized = false)
	{
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
			if (code == 200)
			{
				var text = Encoding.UTF8.GetString(b);
				var doc = JsonDocument.Parse(text);
				tcs.SetResult(doc.RootElement);
			}
			else
			{
				tcs.SetResult(null);
			}

			http.QueueFree();
		};

		http.Request(
			url,
			headers.ToArray(),
			body != null ? HttpClient.Method.Post : HttpClient.Method.Get,
			json
		);

		return await tcs.Task;
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
		using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		file.StoreString(json);    
	}
	private void LoadTokensFromDisk()
	{
		if (!FileAccess.FileExists(SavePath))
			return;

		var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		var text = file.GetAsText();

		var doc = JsonDocument.Parse(text);
		
		AccessToken = doc.RootElement.TryGetProperty("AccessToken", out var a) ? a.GetString() ?? "" : "";
		RefreshToken = doc.RootElement.TryGetProperty("RefreshToken", out var r) ? r.GetString() ?? "" : "";
		Username = doc.RootElement.TryGetProperty("Username", out var u) ? u.GetString() ?? "" : "";
	}

	public void Logout()
	{
		AccessToken = "";
		RefreshToken = "";
		if (FileAccess.FileExists(SavePath))
		{
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
		}
	}
	
	public bool IsLoggedIn()
	{
		return !string.IsNullOrEmpty(AccessToken);
	}
}
