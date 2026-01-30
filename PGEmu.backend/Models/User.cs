using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class User
{
    [Key]
    public Guid Id { get; set; }
    
    [MaxLength(32)]
    public string Username { get; set; } = null!;
    
    [MaxLength(255)]
    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public UserProfile? Profile { get; set; }
    public UserSettings? Settings { get; set; }
    public UserStatus? Status { get; set; }
}