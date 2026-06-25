namespace PGEmuBackend.DTOs.ProfileCustomization;

public class UserGameSummaryDTO
{
    public string ExternalGameId { get; set; } = string.Empty;
    public int PlaytimeMinutes { get; set; }
    public DateTime? LastPlayed { get; set; }
    public string? PlatformId { get; set; }
}
