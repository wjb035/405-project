using PGEmuBackend.Models;


namespace PGEmuBackend.DTOs.Social;

public class FriendDTO
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public FriendStatus Status { get; set; }

}