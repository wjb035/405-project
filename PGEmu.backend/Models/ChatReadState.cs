namespace PGEmuBackend.Models;

public class ChatReadState
{
    public int Id { get; set; }

    public string Username { get; set; } = "";
    public string OtherUser { get; set; } = ""; 

    public DateTime LastReadAt { get; set; }
}