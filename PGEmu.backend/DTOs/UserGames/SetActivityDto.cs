using PGEmuBackend.Models;

public class SetActivityDto
{
    public Guid UserId { get; set; }
    public ActivityType ActivityType { get; set; }
    public string? ExternalGameId { get; set; }
    public GameSource? Source { get; set; }
}