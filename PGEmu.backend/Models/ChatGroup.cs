namespace PGEmuBackend.Models;

public class ChatGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ChatGroupMember> Members { get; set; } = new List<ChatGroupMember>();
}