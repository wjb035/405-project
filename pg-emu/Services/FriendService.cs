using Godot;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Threading.Tasks;
using PGEmu.Services.Models;

namespace PGEmu.Services;

public partial class FriendService : Node
{
	public static FriendService Instance { get; private set; }
	private AuthService Auth => AuthService.Instance;	


	private System.Net.Http.HttpClient httpClient;
	private string baseUrl = "http://localhost:5276/api/friends";
	
	public override void _Ready()
	{
		Instance = this;
		httpClient = new System.Net.Http.HttpClient();
		
	}
	
	private void ApplyAuthHeader()
	{
		var token = AuthService.Instance.AccessToken;

		httpClient.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", token);
	}
	
	public async Task<List<FriendRequestDto>> GetPendingRequests()
	{
		ApplyAuthHeader();
		GD.Print("authththththth");

		var response = await httpClient.GetAsync($"{baseUrl}/pending");
		GD.Print($"HTTP GET /pending status: {response.StatusCode}");
		
		if (!response.IsSuccessStatusCode)
		{
			var text = await response.Content.ReadAsStringAsync();
			GD.Print($"Response body: {text}");
			return new List<FriendRequestDto>();
		}

		var json = await response.Content.ReadAsStringAsync();
		GD.Print($"Response JSON: {json}");
		return JsonSerializer.Deserialize<List<FriendRequestDto>>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});
	}

	public async Task<bool> RespondToRequest(string userId, bool accept)
	{
		ApplyAuthHeader();
		
		var action = accept ? "accept" : "decline";
		var response = await httpClient.PostAsync($"{baseUrl}/{action}/{userId}", null);
		
		return response.IsSuccessStatusCode;
	}

	
	public async void SendFriendRequest(string? userId)
	{
		var response = await Auth.SendAuthorizedRequest(
			$"{baseUrl}/request/{Guid.Parse(userId)}",
			HttpMethod.Post);

		if (response == null)
		{
			GD.Print("Error: SendFriendRequest failed");
			return;
		}
	}
	
	public async void Block(string userId)
	{
		var response = await Auth.SendAuthorizedRequest(
			$"{baseUrl}/block/{userId}",
			HttpMethod.Post);

		if (response == null)
		{
			GD.Print("Error: Block failed");
			return;
		}
	}
		
	public async void Unblock(string userId)
	{
		var response = await Auth.SendAuthorizedRequest(
			$"{baseUrl}/unblock/{userId}",
			HttpMethod.Post);

		if (response == null)
		{
			GD.Print("Error: Unblock failed");
			return;
		}
	}
	
		public async Task<List<FriendRequestDto>> GetBlockedUsers()
	{
		//var response = await Auth.SendAuthorizedRequest(
			//$"{baseUrl}/blocked-users",
			//HttpMethod.Get);
//
		//if (response == null)
		//{
			//GD.Print("Error: GetBlockedUsers failed or no blocked users");
			//var rawJson = response.Value.GetRawText();
			//GD.Print($"RAW JSON: {rawJson}");
			//return null;
		//}
				//
		//var blocked = JsonSerializer.Deserialize<List<FriendRequestDto>>(response.Value.GetRawText(),
		//new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			//
		//return blocked;
		//
	//}
		//ApplyAuthHeader();
//
		////var response = await Auth.SendAuthorizedRequest(
			////$"{baseUrl}/blocked-users",
			////HttpMethod.Get);
//////
		ApplyAuthHeader();
		GD.Print("authththththth");

		var response = await httpClient.GetAsync($"{baseUrl}/blocked-users");
		GD.Print($"HTTP GET /pending status: {response.StatusCode}");
		
		if (!response.IsSuccessStatusCode)
		{
			var text = await response.Content.ReadAsStringAsync();
			GD.Print($"Response body: {text}");
			return new List<FriendRequestDto>();
		}

		var json = await response.Content.ReadAsStringAsync();
		GD.Print($"Response JSON: {json}");
		return JsonSerializer.Deserialize<List<FriendRequestDto>>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});
	}
	
	public async Task<bool> GetIsBlocked(string userId)
	{
		var response = await Auth.SendAuthorizedRequest($"http://localhost:5276/api/friends/blocked/{userId}");
		if (response == null)
		{
			GD.Print("Error: GetIsBlocked failed or session expired");
			return false;
		}
		
		var isBlocked = JsonSerializer.Deserialize<FriendRequestDto>(
			response.Value.GetRawText(),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		
		GD.Print("Username: " + isBlocked.blocked);
		return isBlocked.blocked;
	}
}


public class FriendRequestDto
{
	[JsonPropertyName("id")]
	public string Id { get; set; }
	[JsonPropertyName("username")]
	public string Username { get; set; }
	[JsonPropertyName("status")]
	public FriendStatus Status { get; set; }
	public bool blocked { get; set; }
}

public enum FriendStatus
{
	Pending,
	Accepted,
	Blocked
}
