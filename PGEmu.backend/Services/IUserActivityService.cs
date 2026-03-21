using PGEmuBackend.Models;

public interface IUserActivityService
{
    Task SetActivityAsync(Guid userId, ActivityType type, string? externalGameId = null, GameSource? source = null);
    Task<UserActivity?> GetCurrentActivityAsync(Guid userId);
}