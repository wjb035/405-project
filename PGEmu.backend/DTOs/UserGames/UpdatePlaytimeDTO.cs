using PGEmuBackend.Models;

namespace PGEmuBackend.DTOs.UserGames;

public class UpdatePlaytimeDTO
{
    public string ExternalGameId { get; set; } = null!;
    public GameSource Source { get; set; }
    public int SecondsPlayed { get; set; }
    public string PlatformId { get; set; } = null!; // new field
}