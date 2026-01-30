using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class UserSettings
{
    [Key]
    public Guid UserId { get; set; }

    [Required]
    public string SettingsJson { get; set; } = "{}"; // store JSON as string

    // Navigation
    public User User { get; set; } = null!;
}