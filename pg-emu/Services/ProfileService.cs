using Godot;
using System;
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
	private AuthService Auth => AuthService.Instance;	

	public async Task<ProfileResponse> GetMyProfile()
	{
		var response = await Auth.SendAuthorizedRequest("http://localhost:5276/api/profile/me");
		if (response == null)
		{
			GD.Print("Error: GetMyProfile failed or session expired");
			return null;
		}
		
		var profile = JsonSerializer.Deserialize<ProfileResponse>(
			response.Value.GetRawText(),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		
		if (profile == null || string.IsNullOrEmpty(profile.Username))
		{
			GD.Print("No profile found for this user.");
			return null;
		}
		
		GD.Print("Username: " + profile.Username);
		GD.Print("Bio: " + profile.Bio);
		GD.Print("AvatarUrl: " + profile.AvatarUrl);
		return profile;
	}
	
	public async Task<ProfileResponse> GetUserProfile(string? username)
	{
		var response = await Auth.SendAuthorizedRequest(
			$"http://localhost:5276/api/profile/{Uri.EscapeDataString(username)}");
		
		if (response == null)
		{
			GD.Print("Error: GetUserProfile failed or session expired");
			return null;
		}
		
		
		var profile = JsonSerializer.Deserialize<ProfileResponse>(
			response.Value.GetRawText(),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			
		GD.Print("Username: " + profile.Username);
		GD.Print("Bio: " + profile.Bio);
		GD.Print("AvatarUrl: " + profile.AvatarUrl);
		return profile;
	}
	
		public async Task SetUsername(string? newUsername)
	{
		
		var response = await Auth.SendAuthorizedRequest(
			"http://localhost:5276/api/profile/username",
			new { newUsername },
			HttpMethod.Put);
		
		if (response == null)
		{
			GD.Print("Error: SetUsername failed");
			return;
		}

		GD.Print("Username updated");
	}
	
	
	
	public async Task SetBio(string? newBio)
	{
		var response = await Auth.SendAuthorizedRequest(
			"http://localhost:5276/api/profile/bio",
			new { newBio },
			HttpMethod.Put);

		if (response == null)
		{
			GD.Print("Error: SetBio failed");
			return;
		}

		GD.Print("Bio updated");
	}
	
		public async void SetAvatar(string? newAvatarUrl)
	{
		var response = await Auth.SendAuthorizedRequest(
			"http://localhost:5276/api/profile/avatar",
			new { newAvatarUrl },
			HttpMethod.Put);

		if (response == null)
		{
			GD.Print("Error: SetAvatar failed");
			return;
		}

		GD.Print("Avatar updated");
	}
}
