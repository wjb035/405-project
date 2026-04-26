namespace PGEmuBackend.Models;

public class ChatGroupMember
{
    public int Id { get; set; }
    public string GroupId { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public ChatGroup Group { get; set; } = null!;
}