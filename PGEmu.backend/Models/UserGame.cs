using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class UserGame
{
    [Key]
    public int Id { get; set; }

    public Guid UserId { get; set; }

    [MaxLength(64)]
    public string ExternalGameId { get; set; } = null!;

    [Required]
    public GameSource Source { get; set; }

    public string? InstallPath { get; set; }
    public int PlaytimeMinutes { get; set; } = 0;
    public DateTime? LastPlayed { get; set; }
    public bool Favorite { get; set; } = false;

    // Navigation
    public User User { get; set; } = null!;
}