using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;
using PGEmuBackend.Models;


namespace PGEmuBackend.Services;

public class ProfileCustomizationService
{
    private readonly AppDbContext _context;

    public ProfileCustomizationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task <bool> SetDisplayName(Guid UserId, string displayName)
    {
        var user = await _context.UserProfiles.FindAsync(UserId);
        if (user == null) return false;
        user.DisplayName = displayName;
        await _context.SaveChangesAsync();
        return true;
    }

   
}