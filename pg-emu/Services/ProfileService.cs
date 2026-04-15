using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using PGEmu.Services;
using PGEmu.Services.Models;
using System.Threading.Tasks;
using System.Text;

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

	public async Task<IReadOnlyList<UserSearchResultResponse>> SearchUsersBySimilarity(string? query, int limit = 12)
	{
		var trimmedQuery = query?.Trim();
		if (string.IsNullOrWhiteSpace(trimmedQuery))
			return Array.Empty<UserSearchResultResponse>();

		var clampedLimit = Math.Clamp(limit, 1, 25);
		var response = await Auth.SendAuthorizedRequest(
			$"http://localhost:5276/api/profile/search?query={Uri.EscapeDataString(trimmedQuery)}&limit={clampedLimit}");

		if (response == null)
		{
			GD.Print("Error: SearchUsersBySimilarity failed or session expired");
			return Array.Empty<UserSearchResultResponse>();
		}

		var users = JsonSerializer.Deserialize<List<UserSearchResultResponse>>(
			response.Value.GetRawText(),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		return users ?? new List<UserSearchResultResponse>();
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
	
	public async Task<bool> SetAvatar(string filePath)
	{
		try
		{
			var token = Auth.AccessToken;
			
			
			if (string.IsNullOrEmpty(token))
			{
				GD.Print("Token empty, attempting refresh...");
				await Auth.Refresh();
				token = Auth.AccessToken;
			}
			
			if (string.IsNullOrEmpty(token))
			{
				GD.PrintErr("SetAvatar: No access token available");
				return false;
			}
			using var client = new System.Net.Http.HttpClient();
			client.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

			using var form = new System.Net.Http.MultipartFormDataContent();
			using var fileStream = File.OpenRead(filePath);
			using var streamContent = new System.Net.Http.StreamContent(fileStream);
			
			// Tell the server what kinda file it is
			var extension = Path.GetExtension(filePath).ToLower();
			streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(extension switch
			{
				".png" => "image/png",
				".jpg" or ".jpeg" => "image/jpeg",
				".webp" => "image/webp",
				".gif" => "image/gif",
				_ => "application/octet-stream"
			});
			
			form.Add(streamContent, "file", Path.GetFileName(filePath));

			var response = await client.PostAsync(
				"http://localhost:5276/api/profile/avatar", form);

			if (!response.IsSuccessStatusCode)
			{
				GD.PrintErr($"SetAvatar failed: {(int)response.StatusCode}");
				return false;
			}

			
			GD.Print("Avatar updated");
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"SetAvatar error: {ex.Message}");
			return false;
		}
		
	}
		
	public async Task<bool> DeleteAvatar()
	{
		var response = await Auth.SendAuthorizedRequest(
			"http://localhost:5276/api/profile/avatar",
			null,
			HttpMethod.Delete);

		if (response == null)
		{
			GD.PrintErr("DeleteAvatar failed");
			return false;
		}

		GD.Print("Avatar deleted");
		return true;
	}
}
