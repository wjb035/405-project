using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PGEmuBackend.Models;

public class PasswordResetCode
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required, MaxLength(64)]
    public string CodeHash { get; set; } = null!;

    [Required]
    public DateTime ExpiresAt { get; set; }

    [Required]
    public bool Used { get; set; } = false;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}