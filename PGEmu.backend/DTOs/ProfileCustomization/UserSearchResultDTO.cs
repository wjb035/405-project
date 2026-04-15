namespace PGEmuBackend.DTOs.ProfileCustomization;

public class UserSearchResultDTO
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}
