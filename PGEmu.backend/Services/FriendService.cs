using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;
using PGEmuBackend.Models;
using PGEmuBackend.DTOs.Social;

namespace PGEmuBackend.Services;

public class FriendService
{
    private readonly AppDbContext _context;
    
    public FriendService(AppDbContext context)
    {
        _context = context;
    }
    
    // Send a friend reuqest
    public async Task<bool> SendRequestAsync(Guid senderId, Guid receiverId)
    {
        
        // Cant friend yourself
        if (senderId == receiverId)
            return false;

        // Make sure target account exists
        if (!await _context.Users.AnyAsync(u => u.Id == receiverId))
            return false;
        
        // Check if friend record exists in either direction
        var existing = await _context.Friends
            .FirstOrDefaultAsync(f =>
                (f.SenderId == senderId && f.ReceiverId == receiverId) ||
                (f.SenderId == receiverId && f.ReceiverId == senderId));

        if (existing != null)
        {
            if (existing.Status == FriendStatus.Accepted || existing.Status == FriendStatus.Blocked)
                return false;

            if ((existing.SenderId == receiverId && existing.ReceiverId == senderId) &&
                existing.Status == FriendStatus.Pending)
            {
                existing.Status = FriendStatus.Accepted;
                await _context.SaveChangesAsync();
                return true; // auto-accepted
            }

            return false; // pending already exists in either direction
        }

        // Create pending friend request
        var friend = new Friend
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Status = FriendStatus.Pending
        };
        
        // Save the recrod
        _context.Friends.Add(friend);
        await _context.SaveChangesAsync();

        return true;
        
    }

    // Accept a friend request
    public async Task<bool> AcceptRequestAsync(Guid receiverId, Guid senderId)
    {
        // Find the pending request where sender sent to receiver
        var friend = await _context.Friends
            .FirstOrDefaultAsync(f =>
                f.SenderId == senderId &&
                f.ReceiverId == receiverId &&
                f.Status == FriendStatus.Pending);
        
        // Check if accounts aren't already friended 
        if (friend == null)
            return false;

        friend.Status = FriendStatus.Accepted;
        await _context.SaveChangesAsync();
        return true;
    }
    
    // Decline a friend reuqest
    public async Task<bool> DeclineRequestAsync(Guid receiverId, Guid senderId)
    {
        // Find the pending request where sender sent to receiver
        var friend = await _context.Friends
            .FirstOrDefaultAsync(f =>
                f.SenderId == senderId &&
                f.ReceiverId == receiverId &&
                f.Status == FriendStatus.Pending);
        
        if (friend == null)
            return false;

        // Remove the friend request
        _context.Friends.Remove(friend);
        await _context.SaveChangesAsync();
        return true;
    }
    
    // Block a user
    public async Task<bool> BlockUserAsync(Guid blockerId, Guid blockedId)
    {
        // Check if a record exists in either direction
        var friend = await _context.Friends
            .FirstOrDefaultAsync(f =>
                (f.SenderId == blockerId && f.ReceiverId == blockedId) ||
                (f.SenderId == blockedId && f.ReceiverId == blockerId));

        // If the accounts are friends, block it. If they arent friends, block it anyways.
        if (friend == null)
        {
            friend = new Friend
            {
                SenderId = blockerId,
                ReceiverId = blockedId,
                Status = FriendStatus.Blocked
            };
            _context.Friends.Add(friend);
        }
        else
        {
            friend.Status = FriendStatus.Blocked;
        }
        
        await _context.SaveChangesAsync();
        return true;
    }
    
    //UNblock a user
    public async Task<bool> UnblockUserAsync(Guid unblockerId, Guid blockedId)
    {
        // Check if a record exists in either direction
        var friend = await _context.Friends
            .FirstOrDefaultAsync(f =>
                (f.SenderId == unblockerId && f.ReceiverId == blockedId) ||
                (f.SenderId == blockedId && f.ReceiverId == unblockerId));

        // If not friends, nothing to unblock
        if (friend == null)
            return false;
        
        // Not blocked, cant unlblock
        if (friend.Status != FriendStatus.Blocked)
            return false; 
        
        // Remove all friend record, which means you can now re friend etc
        _context.Friends.Remove(friend);
        
        await _context.SaveChangesAsync();
        return true;
    }
    
    // Get all friends of user
    public async Task<List<FriendDTO>> GetFriendsAsync(Guid userId)
    {
        // Find accoeted fruebdsguos
        var friends = await _context.Friends
            .Where(f =>
                f.Status == FriendStatus.Accepted &&
                (f.SenderId == userId || f.ReceiverId == userId))
            .ToListAsync();

        // Get IDs of friends
        var friendIds = friends
            .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
            .ToList();

        // Returns a list of friends as Users
        return await _context.Users
            .Where(u => friendIds.Contains(u.Id))
            .Select(u => new FriendDTO
            {
                Id = u.Id,
                Username = u.Username
            })
            .ToListAsync();
    }

    // Get all pending requests for a user
    public async Task<List<FriendDTO>> GetPendingRequestsAsync(Guid receiverId)
    {
        // Find all requests where this user is the receiver and status is pending
        var pendingFriendships = await _context.Friends
            .Where(f => f.ReceiverId == receiverId && f.Status == FriendStatus.Pending)
            .ToListAsync();
        
        
        // Determine the other user in each friendship
        var senderIds = pendingFriendships
            .Select(f => f.SenderId)
            .ToList();
        
        // Return minimal user info for each requester
        return await _context.Users
            .Where(u => senderIds.Contains(u.Id))
            .Select(u => new FriendDTO
            {
                Id = u.Id,
                Username = u.Username
            })
            .ToListAsync();
    }
}