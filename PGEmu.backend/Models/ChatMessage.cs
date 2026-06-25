using System;

namespace PGEmuBackend.Models;

public class ChatMessage
{
    public int Id { get; set; }
    public string FromUser { get; set; } = "";
    public string ToUser { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsGroupMessage { get; set; } = false;
}