using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class UserProfile
{
    public Guid UserId { get; set; }

    [MaxLength(32)]
    public string? DisplayName { get; set; }

    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }

    [MaxLength(32)]
    public string Theme { get; set; } = "system";

    [MaxLength(16)]
    public string Language { get; set; } = "en-US";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}