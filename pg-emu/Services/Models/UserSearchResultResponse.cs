namespace PGEmu.Services.Models;

public class UserSearchResultResponse
{
	public string UserId { get; set; } = string.Empty;
	public string Username { get; set; } = string.Empty;
	public string? AvatarUrl { get; set; }
}
