using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;
using PGEmuBackend.Models;
using PGEmuBackend.DTOs.ProfileCustomization;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.OpenApi;
using static System.IO.Path;


namespace PGEmuBackend.Services;

public class ProfileCustomizationService : IProfileCustomizationService
{
    private readonly AppDbContext _context;
    private const int MaximumSearchLimit = 25;
    private const int CandidateFetchCount = 250;

    public ProfileCustomizationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Success, string Message, string? NewUsername)>
        ChangeUsernameAsync(Guid UserId, string newUsername)
    {
        if (string.IsNullOrWhiteSpace(newUsername))
            return (false, "Username cannot be empty", null);

        if (newUsername.Length < 3 || newUsername.Length > 32)
            return (false, "Username must be between 3 and 20 characters", null);

        if (await _context.Users.AnyAsync(u => u.Username == newUsername))
            return (false, "Username already taken", null);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == UserId);

        if (user == null)
            return (false, "User not found", null);

        user.Username = newUsername;

        await _context.SaveChangesAsync();

        return (true, "Username changed successfully", user.Username);
    }



    public async Task<(bool Success, string Message, string? NewBio)> ChangeBioAsync(Guid userId, string? newBio)
    {
        var user = await _context.Users.FindAsync(userId);
        var userProfile = await _context.UserProfiles.FindAsync(userId);

        if (user == null) return (false, "User not found", null);

        if (newBio.Length > 250)
            return (false, "Bio must be less than 250 characters", null);

        if (user.Profile == null)
        {
            user.Profile = new UserProfile
            {
                UserId = userId,
                Bio = newBio
            };
        }
        else
            userProfile.Bio = newBio;
        await _context.SaveChangesAsync();

        return (true, "Bio changed successfully", user.Profile.Bio);
    }

    public async Task<(bool Success, string Message, string? NewAvatarUrl)> ChangeAvatarAsync(Guid userId, string? newAvatarUrl)
    {
        var user = await _context.Users.FindAsync(userId);
        var userProfile = await _context.UserProfiles.FindAsync(userId);

        if (user == null) return (false, "User not found", null);

        if (user.Profile == null)
        {
            user.Profile = new UserProfile
            {
                UserId = userId,
                AvatarUrl = newAvatarUrl
            };
        }
        else
            userProfile.AvatarUrl = newAvatarUrl;
        await _context.SaveChangesAsync();

        return (true, "Avatar changed successfully", user.Profile.AvatarUrl);
    }




    public async Task<(bool Success, string Message, ProfileCustomizationDTO Profile)> GetUserAsync(Guid? UserId, string? username)
    {
        User user = null;
        UserProfile? userProfile = null;
        if (UserId != null)
        {
            user = await _context.Users.FindAsync(UserId);
        }
        else if (username != null)
        {
            user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        }

        if (user == null) return (false, "User not found.", null);

        await _context.Entry(user).Reference(u => u.Profile).LoadAsync();

        if (user.Profile == null)
        {
            user.Profile = new UserProfile
            {
                UserId = user.Id,
            };
        }

        userProfile = user.Profile;

        Console.WriteLine(user.Username);
        return (true, "User found",
            new ProfileCustomizationDTO
            {
                UserId = user.Id,
                Username = user.Username,
                Bio = user.Profile.Bio,
                AvatarUrl = user.Profile.AvatarUrl,
            });
    }

    public async Task<IReadOnlyList<UserSearchResultDTO>> SearchUsersBySimilarityAsync(Guid currentUserId, string query, int limit)
    {
        var normalizedQuery = query?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return Array.Empty<UserSearchResultDTO>();

        var resolvedLimit = Math.Clamp(limit, 1, MaximumSearchLimit);
        var containsPattern = $"%{normalizedQuery}%";
        var prefixPattern = $"{normalizedQuery}%";
        var maxLengthDiff = Math.Max(2, normalizedQuery.Length / 2);

        var candidates = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id != currentUserId)
            .Select(u => new
            {
                u.Id,
                u.Username,
                AvatarUrl = u.Profile != null ? u.Profile.AvatarUrl : null,
                LowerUsername = u.Username.ToLower(),
                LengthDiff = Math.Abs(u.Username.Length - normalizedQuery.Length)
            })
            .Where(user =>
                EF.Functions.Like(user.LowerUsername, prefixPattern) ||
                EF.Functions.Like(user.LowerUsername, containsPattern) ||
                user.LengthDiff <= maxLengthDiff)
            .OrderByDescending(user => EF.Functions.Like(user.LowerUsername, prefixPattern))
            .ThenByDescending(user => EF.Functions.Like(user.LowerUsername, containsPattern))
            .ThenBy(user => user.LengthDiff)
            .ThenBy(user => user.Username)
            .Take(CandidateFetchCount)
            .ToListAsync();

        var rankedUsers = candidates
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Username,
                candidate.AvatarUrl,
                Score = ComputeSearchSimilarity(candidate.Username, normalizedQuery)
            })
            .Where(candidate => candidate.Score > 0f)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Username, StringComparer.OrdinalIgnoreCase)
            .Take(resolvedLimit)
            .Select(candidate => new UserSearchResultDTO
            {
                UserId = candidate.Id,
                Username = candidate.Username,
                AvatarUrl = candidate.AvatarUrl
            })
            .ToArray();

        return rankedUsers;
    }

    private static float ComputeSearchSimilarity(string candidate, string query)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(query))
            return 0f;

        var candidateLower = candidate.Trim().ToLowerInvariant();
        var queryLower = query.Trim().ToLowerInvariant();

        if (candidateLower == queryLower)
            return 1000f;

        float score = 0f;
        if (candidateLower.StartsWith(queryLower, StringComparison.Ordinal))
            score += 450f;
        if (candidateLower.Contains(queryLower, StringComparison.Ordinal))
            score += 220f;

        var maxPrefixLength = Math.Min(candidateLower.Length, queryLower.Length);
        int commonPrefixLength = 0;
        for (; commonPrefixLength < maxPrefixLength; commonPrefixLength++)
        {
            if (candidateLower[commonPrefixLength] != queryLower[commonPrefixLength])
                break;
        }

        score += commonPrefixLength * 38f;
        score += ComputeBigramOverlap(candidateLower, queryLower) * 220f;
        score -= Math.Abs(candidateLower.Length - queryLower.Length) * 3f;

        return score > 0f ? score : 0f;
    }

    private static float ComputeBigramOverlap(string left, string right)
    {
        if (left.Length < 2 || right.Length < 2)
            return left == right ? 1f : 0f;

        var leftBigrams = new HashSet<string>();
        for (int index = 0; index < left.Length - 1; index++)
            leftBigrams.Add(left.Substring(index, 2));

        int overlap = 0;
        int rightCount = 0;
        for (int index = 0; index < right.Length - 1; index++)
        {
            rightCount++;
            if (leftBigrams.Contains(right.Substring(index, 2)))
                overlap++;
        }

        var divisor = Math.Max(leftBigrams.Count, rightCount);
        return divisor == 0 ? 0f : (float)overlap / divisor;
    }
}
