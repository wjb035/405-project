using Godot;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using PGEmu.Services;
using PGEmu.Services.Models;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace PGEmu.Services;


public partial class ProfileService : Node
{
	private readonly System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();
	
	private void ApplyAuthHeader()
	{
		var token = AuthService.Instance.AccessToken;
		GD.Print(token);

		_client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);;
	}
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	
	public async Task<ProfileResponse> GetMyProfile()
	{
		ApplyAuthHeader();
		
		var response = await _client.GetAsync("http://localhost:5276/api/profile/me");
		
		if (!response.IsSuccessStatusCode)
		{
			GD.Print("Error: " + response.StatusCode);
			return null;
		}
		
		var json = await response.Content.ReadAsStringAsync();
		
		var profile = JsonSerializer.Deserialize<ProfileResponse>(json,
			new JsonSerializerOptions {PropertyNameCaseInsensitive = true});
			
		GD.Print("Username: " + profile.Username);
		GD.Print("Bio: " + profile.Bio);
		GD.Print("AvatarUrl: " + profile.AvatarUrl);
		return profile;
	}
	
		public async void SetUsername(string? newUsername)
	{
		ApplyAuthHeader();
		
		var requestContent = new 
		{
			newUsername = newUsername
		};
		
		var json = JsonSerializer.Serialize(requestContent);
		var content = new StringContent(json, Encoding.UTF8, "application/json");
		
		var response = await _client.PutAsync(("http://localhost:5276/api/profile/username"), content);
		
		if (!response.IsSuccessStatusCode)
		{
			GD.Print("Error: " + response.StatusCode);
			return;
		}
		
		json = await response.Content.ReadAsStringAsync();
		
		var profile = JsonSerializer.Deserialize<ProfileResponse>(json,
			new JsonSerializerOptions {PropertyNameCaseInsensitive = true});
			
		GD.Print("Username: " + profile.Username);
		GD.Print("Bio: " + profile.Bio);
	}
	
	
	
	public async void SetBio(string? newBio)
	{
		ApplyAuthHeader();
		
		var requestContent = new 
		{
			newBio = newBio
		};
		
		var json = JsonSerializer.Serialize(requestContent);
		var content = new StringContent(json, Encoding.UTF8, "application/json");
		
		var response = await _client.PutAsync(("http://localhost:5276/api/profile/bio"), content);
		
		if (!response.IsSuccessStatusCode)
		{
			GD.Print("Error: " + response.StatusCode);
			return;
		}
		
		json = await response.Content.ReadAsStringAsync();
		
		var profile = JsonSerializer.Deserialize<ProfileResponse>(json,
			new JsonSerializerOptions {PropertyNameCaseInsensitive = true});
			
		GD.Print("Username: " + profile.Username);
		GD.Print("Bio: " + profile.Bio);
	}
	
		public async void SetAvatar(string? newAvatarUrl)
	{
		ApplyAuthHeader();
		
		var requestContent = new 
		{
			newAvatarUrl = newAvatarUrl
		};
		
		var json = JsonSerializer.Serialize(requestContent);
		var content = new StringContent(json, Encoding.UTF8, "application/json");
		
		var response = await _client.PutAsync(("http://localhost:5276/api/profile/avatar"), content);
		
		if (!response.IsSuccessStatusCode)
		{
			GD.Print("Error: " + response.StatusCode);
			return;
		}
		
		json = await response.Content.ReadAsStringAsync();
		
		var profile = JsonSerializer.Deserialize<ProfileResponse>(json,
			new JsonSerializerOptions {PropertyNameCaseInsensitive = true});
			
		GD.Print("Username: " + profile.Username);
		GD.Print("Bio: " + profile.Bio);
	}
}
