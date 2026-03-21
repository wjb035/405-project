using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;
using PGEmuBackend.Models;

public class UserActivityService : IUserActivityService
{
    private readonly AppDbContext _context;

    public UserActivityService(AppDbContext context)
    {
        _context = context;
    }

    public async Task SetActivityAsync(Guid userId, ActivityType type, string? externalGameId = null, GameSource? source = null)
    {
        var activity = await _context.UserActivities
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (activity == null)
        {
            activity = new UserActivity
            {
                UserId = userId
            };

            _context.UserActivities.Add(activity);
        }

        activity.ActivityType = type;
        activity.ExternalGameId = externalGameId;
        activity.Source = source;
        activity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task<UserActivity?> GetCurrentActivityAsync(Guid userId)
    {
        return await _context.UserActivities
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);
    }
}