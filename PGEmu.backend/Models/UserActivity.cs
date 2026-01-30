using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class UserActivity
{
    [Key]
    public int Id { get; set; }

    public Guid UserId { get; set; }

    [Required]
    [EnumDataType(typeof(ActivityType))]
    public ActivityType ActivityType { get; set; }
    
    [MaxLength(64)]
    public string? ExternalGameId { get; set; }
    
    [EnumDataType(typeof(GameSource))]
    public GameSource? Source { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public User User { get; set; } = null!;
    
    
}

public enum ActivityType
{
    Play,
    Favorite,
    CollectionAdd
}

public enum GameSource
{
    Igdb,
    Libretro,
    Custom
}