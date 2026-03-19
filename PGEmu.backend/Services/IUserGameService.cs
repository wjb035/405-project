using PGEmuBackend.DTOs.UserGames;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IUserGameService
{
    Task<(string gameName, int playtimeMinutes, string platformId)> UpdatePlaytimeAsync(Guid userId, UpdatePlaytimeDTO request);
}