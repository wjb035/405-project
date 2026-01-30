using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class UserStatus
{
    [Key]
    public Guid UserId { get; set; }

    public bool Online { get; set; } = false;

    [MaxLength(32)]
    public string Status { get; set; } = "offline";

    public DateTime? LastLogin { get; set; }
    public DateTime? LastSeen { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}