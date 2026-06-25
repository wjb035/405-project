namespace PGEmuBackend.DTOs.UserGames
{
    public class UserGameDTO
    {
        public string ExternalGameId { get; set; }
        public int PlaytimeMinutes { get; set; }
        public string PlatformId { get; set; }
        public int Source { get; set; }
    }
}