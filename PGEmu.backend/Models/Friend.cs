using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class Friend
{
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }

    [Required]
    [EnumDataType(typeof(FriendStatus))]
    public FriendStatus Status { get; set; } = FriendStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User Sender { get; set; } = null!;
    public User Receiver { get; set; } = null!;
}

public enum FriendStatus
{
    Pending,
    Accepted,
    Blocked
}
