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




    public async Task<(bool Success, string Message, ProfileCustomizationDTO Profile)> GetUserAsync(Guid UserId)
    {

        User user = await _context.Users.FindAsync(UserId);
        UserProfile? userProfile = await _context.UserProfiles.FindAsync(UserId);

        if (user == null) return (false, "User not found.", null);
        
        // Create profile row if it doesn't exist
        if (userProfile == null)
        {
            userProfile = new UserProfile
            {
                UserId = UserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _context.UserProfiles.AddAsync(userProfile);
            await _context.SaveChangesAsync();
        }
        
        return (true, "User found",
            new ProfileCustomizationDTO
        {
            UserId = user.Id,
            Username = user.Username,
            Bio = userProfile.Bio,
            AvatarUrl = userProfile.AvatarUrl,
            });
    }
}