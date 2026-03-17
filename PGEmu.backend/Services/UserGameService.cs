using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;
using PGEmuBackend.DTOs.UserGames;
using PGEmuBackend.Models;

namespace PGEmuBackend.Services;

public class UserGameService : IUserGameService
{
    private readonly AppDbContext _context;

    public UserGameService(AppDbContext context)
    {
        _context = context;
    }

    

    public async Task<(string gameName, int playtimeMinutes, string? platformId)> UpdatePlaytimeAsync(Guid userId, UpdatePlaytimeDTO request)
    {
        var game = await _context.UserGames
            .FirstOrDefaultAsync(g =>
                g.UserId == userId &&
                g.ExternalGameId == request.ExternalGameId &&
                g.Source == request.Source);

        if (game != null)
        {
            // Update existing
            game.PlaytimeMinutes += request.SecondsPlayed / 60;
            game.LastPlayed = DateTime.UtcNow;
            game.InstallPath = request.PlatformId; // store platform ID
        }
        else
        {
            // Create new
            game = new UserGame
            {
                UserId = userId,
                ExternalGameId = request.ExternalGameId,
                Source = request.Source,
                PlaytimeMinutes = request.SecondsPlayed / 60,
                LastPlayed = DateTime.UtcNow,
                InstallPath = request.PlatformId // store platform ID
            };

            _context.UserGames.Add(game);
        }

        await _context.SaveChangesAsync();

        return (game.ExternalGameId, game.PlaytimeMinutes, game.InstallPath); // include platform ID
    }
}