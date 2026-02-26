namespace PGEmuBackend.DTOs.ProfileCustomization;

public class ProfileCustomization
{
    public Guid UserId { get; }

    public string? UserName { get; set; }

    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public DateTime CreatedAt { get; }
}