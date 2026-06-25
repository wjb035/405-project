using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PGEmuBackend.Models;

namespace PGEmuBackend.Data;

public sealed class DevelopmentDataSeeder
{
    public const string SeedPassword = "PGEmuTest123!";

    private static readonly SeedAccountDefinition[] SeedAccounts =
    {
        new(
            Username: "HuzzFinder",
            Bio: "Lives in hidden menus, obscure ROM hacks, and weird import box art. Calls every backlog a research archive.",
            AvatarFileName: "dev-seed-huzzfinder.png",
            Online: true,
            StatusText: "online",
            CreatedDaysAgo: 120,
            ActivityType: ActivityType.Play,
            ActivityGameTitle: "Ape Escape 3 (USA)",
            ActivityUpdatedMinutesAgo: 2,
            Games:
            [
                new SeedGameDefinition("Ape Escape 3 (USA)", GameSource.Custom, "ps2", 415, 1, true),
                new SeedGameDefinition("Lumines II", GameSource.Custom, "psp", 220, 3, false),
                new SeedGameDefinition("Monster Hunter Freedom Unite", GameSource.Custom, "psp", 980, 0, true)
            ]),
        new(
            Username: "ObeseG00n3r",
            Bio: "Swears every fighting game tier list is propaganda. Logs serious hours in arcade modes and never skips character select music.",
            AvatarFileName: "dev-seed-obeseg00n3r.png",
            Online: true,
            StatusText: "grinding",
            CreatedDaysAgo: 95,
            ActivityType: ActivityType.Play,
            ActivityGameTitle: "Tekken: Dark Resurrection",
            ActivityUpdatedMinutesAgo: 4,
            Games:
            [
                new SeedGameDefinition("Tekken: Dark Resurrection", GameSource.Custom, "psp", 760, 0, true),
                new SeedGameDefinition("Burnout Dominator", GameSource.Custom, "psp", 315, 6, false),
                new SeedGameDefinition("Tony Hawk's Underground 2 Remix", GameSource.Custom, "psp", 455, 2, true)
            ]),
        new(
            Username: "JonesyFrmFortnite",
            Bio: "Chronically in the middle of a crossover event. Pretends every shooter has lore worth studying like scripture.",
            AvatarFileName: "dev-seed-jonesyfrmfortnite.png",
            Online: false,
            StatusText: "away",
            CreatedDaysAgo: 80,
            ActivityType: ActivityType.Play,
            ActivityGameTitle: "SOCOM: Fireteam Bravo 2",
            ActivityUpdatedMinutesAgo: 14,
            Games:
            [
                new SeedGameDefinition("SOCOM: Fireteam Bravo 2", GameSource.Custom, "psp", 610, 1, true),
                new SeedGameDefinition("Ratchet & Clank: Size Matters", GameSource.Custom, "psp", 205, 5, false),
                new SeedGameDefinition("Medal of Honor: Heroes", GameSource.Custom, "psp", 340, 8, false)
            ]),
        new(
            Username: "Your Actual Mother",
            Bio: "Plays puzzle games with ruthless efficiency and then casually clears your favorite rhythm game on the first try.",
            AvatarFileName: "dev-seed-your-actual-mother.png",
            Online: true,
            StatusText: "online",
            CreatedDaysAgo: 140,
            ActivityType: ActivityType.Favorite,
            ActivityGameTitle: "Lumines",
            ActivityUpdatedMinutesAgo: 1,
            Games:
            [
                new SeedGameDefinition("Lumines", GameSource.Custom, "psp", 840, 0, true),
                new SeedGameDefinition("Pac-Man Championship Edition", GameSource.Custom, "psp", 140, 11, false),
                new SeedGameDefinition("Every Extend Extra", GameSource.Custom, "psp", 265, 9, true)
            ]),
        new(
            Username: "SouljaBoyTellem",
            Bio: "Treats every game library like a mixtape drop. Favorites menus, flashy fighters, and anything with loud startup screens.",
            AvatarFileName: "dev-seed-souljaboytellem.png",
            Online: true,
            StatusText: "broadcasting",
            CreatedDaysAgo: 60,
            ActivityType: ActivityType.Play,
            ActivityGameTitle: "DJ Max Portable 3",
            ActivityUpdatedMinutesAgo: 3,
            Games:
            [
                new SeedGameDefinition("DJ Max Portable 3", GameSource.Custom, "psp", 520, 0, true),
                new SeedGameDefinition("Soulcalibur: Broken Destiny", GameSource.Custom, "psp", 410, 4, true),
                new SeedGameDefinition("Def Jam: Fight for NY - The Takeover", GameSource.Custom, "psp", 355, 7, false)
            ])
    };

    private readonly AppDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DevelopmentDataSeeder> _logger;

    public DevelopmentDataSeeder(
        AppDbContext context,
        IPasswordHasher<User> passwordHasher,
        IWebHostEnvironment environment,
        ILogger<DevelopmentDataSeeder> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _environment = environment;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        var seedUsernames = SeedAccounts
            .Select(account => account.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingSeedUsers = await _context.Users
            .Include(user => user.Profile)
            .Include(user => user.Status)
            .Where(user => seedUsernames.Contains(user.Username))
            .ToDictionaryAsync(user => user.Username, StringComparer.OrdinalIgnoreCase);

        foreach (var account in SeedAccounts)
        {
            if (!existingSeedUsers.TryGetValue(account.Username, out var user))
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Username = account.Username,
                    Email = BuildSeedEmail(account.Username),
                    CreatedAt = DateTime.UtcNow.AddDays(-account.CreatedDaysAgo)
                };
                user.PasswordHash = _passwordHasher.HashPassword(user, SeedPassword);
                user.Profile = new UserProfile
                {
                    UserId = user.Id,
                    DisplayName = account.Username,
                    AvatarUrl = BuildAvatarUrl(account.AvatarFileName),
                    Bio = account.Bio,
                    Theme = "system",
                    Language = "en-US",
                    CreatedAt = DateTime.UtcNow.AddDays(-account.CreatedDaysAgo),
                    UpdatedAt = DateTime.UtcNow
                };
                user.Status = new UserStatus
                {
                    UserId = user.Id,
                    Online = account.Online,
                    Status = account.StatusText,
                    LastLogin = DateTime.UtcNow.AddMinutes(-account.ActivityUpdatedMinutesAgo),
                    LastSeen = DateTime.UtcNow.AddMinutes(-account.ActivityUpdatedMinutesAgo)
                };

                _context.Users.Add(user);
                existingSeedUsers[account.Username] = user;
                continue;
            }

            user.Email = BuildSeedEmail(account.Username);

            user.Profile ??= new UserProfile
            {
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-account.CreatedDaysAgo)
            };
            user.Profile.DisplayName = account.Username;
            user.Profile.AvatarUrl = BuildAvatarUrl(account.AvatarFileName);
            user.Profile.Bio = account.Bio;
            user.Profile.Theme = "system";
            user.Profile.Language = "en-US";
            user.Profile.UpdatedAt = DateTime.UtcNow;

            user.Status ??= new UserStatus { UserId = user.Id };
            user.Status.Online = account.Online;
            user.Status.Status = account.StatusText;
            user.Status.LastLogin = DateTime.UtcNow.AddMinutes(-account.ActivityUpdatedMinutesAgo);
            user.Status.LastSeen = DateTime.UtcNow.AddMinutes(-account.ActivityUpdatedMinutesAgo);
        }

        await _context.SaveChangesAsync();

        foreach (var account in SeedAccounts)
        {
            var user = existingSeedUsers[account.Username];
            await EnsureSeedGamesAsync(user.Id, account.Games);
            await EnsureSeedActivityAsync(user.Id, account);
        }

        var seededUsers = existingSeedUsers.Values.ToList();
        await EnsureFriendshipsBetweenSeedUsersAsync(seededUsers);
        await EnsureFriendshipsForLocalUsersAsync(seededUsers, seedUsernames);

        await _context.SaveChangesAsync();
        _logger.LogInformation("Development seed ensured for {Count} fake accounts. Shared password: {Password}", SeedAccounts.Length, SeedPassword);
    }

    private async Task EnsureSeedGamesAsync(Guid userId, IReadOnlyList<SeedGameDefinition> games)
    {
        var existingGames = await _context.UserGames
            .Where(game => game.UserId == userId)
            .ToListAsync();

        foreach (var seedGame in games)
        {
            var existing = existingGames.FirstOrDefault(game =>
                game.ExternalGameId == seedGame.GameTitle &&
                game.Source == seedGame.Source);

            if (existing == null)
            {
                _context.UserGames.Add(new UserGame
                {
                    UserId = userId,
                    ExternalGameId = seedGame.GameTitle,
                    Source = seedGame.Source,
                    InstallPath = seedGame.PlatformId,
                    PlaytimeMinutes = seedGame.PlaytimeMinutes,
                    LastPlayed = DateTime.UtcNow.AddDays(-seedGame.LastPlayedDaysAgo),
                    Favorite = seedGame.Favorite
                });
                continue;
            }

            existing.InstallPath = seedGame.PlatformId;
            existing.PlaytimeMinutes = seedGame.PlaytimeMinutes;
            existing.LastPlayed = DateTime.UtcNow.AddDays(-seedGame.LastPlayedDaysAgo);
            existing.Favorite = seedGame.Favorite;
        }
    }

    private async Task EnsureSeedActivityAsync(Guid userId, SeedAccountDefinition account)
    {
        var existingActivity = await _context.UserActivities
            .FirstOrDefaultAsync(activity => activity.UserId == userId);

        if (existingActivity == null)
        {
            _context.UserActivities.Add(new UserActivity
            {
                UserId = userId,
                ActivityType = account.ActivityType,
                ExternalGameId = account.ActivityGameTitle,
                Source = GameSource.Custom,
                UpdatedAt = DateTime.UtcNow.AddMinutes(-account.ActivityUpdatedMinutesAgo)
            });
            return;
        }

        existingActivity.ActivityType = account.ActivityType;
        existingActivity.ExternalGameId = account.ActivityGameTitle;
        existingActivity.Source = GameSource.Custom;
        existingActivity.UpdatedAt = DateTime.UtcNow.AddMinutes(-account.ActivityUpdatedMinutesAgo);
    }

    private async Task EnsureFriendshipsBetweenSeedUsersAsync(IReadOnlyList<User> seededUsers)
    {
        for (int left = 0; left < seededUsers.Count; left++)
        {
            for (int right = left + 1; right < seededUsers.Count; right++)
                await EnsureAcceptedFriendshipAsync(seededUsers[left].Id, seededUsers[right].Id);
        }
    }

    private async Task EnsureFriendshipsForLocalUsersAsync(IReadOnlyList<User> seededUsers, ISet<string> seedUsernames)
    {
        var localUsers = await _context.Users
            .Where(user => !seedUsernames.Contains(user.Username))
            .ToListAsync();

        foreach (var localUser in localUsers)
        {
            foreach (var seededUser in seededUsers)
                await EnsureAcceptedFriendshipAsync(localUser.Id, seededUser.Id);
        }
    }

    private async Task EnsureAcceptedFriendshipAsync(Guid firstUserId, Guid secondUserId)
    {
        if (firstUserId == secondUserId)
            return;

        var existing = await _context.Friends.FirstOrDefaultAsync(friend =>
            (friend.SenderId == firstUserId && friend.ReceiverId == secondUserId) ||
            (friend.SenderId == secondUserId && friend.ReceiverId == firstUserId));

        if (existing == null)
        {
            _context.Friends.Add(new Friend
            {
                SenderId = firstUserId,
                ReceiverId = secondUserId,
                Status = FriendStatus.Accepted,
                CreatedAt = DateTime.UtcNow.AddDays(-14)
            });
            return;
        }

        existing.Status = FriendStatus.Accepted;
    }

    private static string BuildSeedEmail(string username)
    {
        var slug = new string(username
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '.')
            .ToArray())
            .Trim('.');

        return $"{slug}@seed.pgemu.local";
    }

    private static string BuildAvatarUrl(string avatarFileName)
    {
        return $"/uploads/avatars/{avatarFileName}";
    }

    private sealed record SeedAccountDefinition(
        string Username,
        string Bio,
        string AvatarFileName,
        bool Online,
        string StatusText,
        int CreatedDaysAgo,
        ActivityType ActivityType,
        string ActivityGameTitle,
        int ActivityUpdatedMinutesAgo,
        IReadOnlyList<SeedGameDefinition> Games);

    private sealed record SeedGameDefinition(
        string GameTitle,
        GameSource Source,
        string PlatformId,
        int PlaytimeMinutes,
        int LastPlayedDaysAgo,
        bool Favorite);
}
