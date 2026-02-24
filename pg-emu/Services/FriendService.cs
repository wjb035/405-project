using Godot;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PGEmu.Services;

public partial class FriendService : Node
{
    public static FriendService Instance { get; private set; }

    private System.Net.Http.HttpClient httpClient;
    private string baseUrl = "http://localhost:5276/api/friends/";
    
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

        var response = await httpClient.GetAsync($"{baseUrl}/pending");

        if (!response.IsSuccessStatusCode)
            return new List<FriendRequestDto>();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<FriendRequestDto>>(json);
    }

    public async Task<bool> RespondToRequest(string userId, bool accept)
    {
        ApplyAuthHeader();
        
        var action = accept ? "accept" : "decline";
        var response = await httpClient.PostAsync($"{baseUrl}/{action}/{userId}", null);
        
        return response.IsSuccessStatusCode;
    }
    
}

public class FriendRequestDto
{
    public string Id { get; set; }
    public string Username { get; set; }
}