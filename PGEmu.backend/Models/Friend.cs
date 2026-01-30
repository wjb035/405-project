using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class Friend
{
    public Guid UserId { get; set; }
    public Guid FriendId { get; set; }

    [Required]
    [EnumDataType(typeof(FriendStatus))]
    public FriendStatus Status { get; set; } = FriendStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public User FriendUser { get; set; } = null!;
}

public enum FriendStatus
{
    Pending,
    Accepted,
    Blocked
}
