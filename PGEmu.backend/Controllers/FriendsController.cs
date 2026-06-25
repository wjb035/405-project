using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PGEmuBackend.DTOs.Social;
using PGEmuBackend.Services;
using PGEmuBackend.Models;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/friends")]
public class FriendsController : ControllerBase
{
    private readonly FriendService _friendService;

    public FriendsController(FriendService friendService)
    {
        _friendService = friendService;
    }

    // Helper to get current user
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirst("sub")?.Value ?? throw new Exception("User not authenticated"));


    //HTTP Endpoints for friend actions
    [Authorize]
    [HttpPost("request/{targetUserId}")]
    public async Task<IActionResult> SendRequest(Guid targetUserId)
    {
        var success = await _friendService.SendRequestAsync(CurrentUserId, targetUserId);
        if (!success) return BadRequest("Cannot send friend request.");
        return Ok(new { message = "Friend request sent." });
    }

    [Authorize] 
    [HttpPost("accept/{requesterId}")]
    public async Task<IActionResult> AcceptRequest(Guid requesterId)
    {
        var success = await _friendService.AcceptRequestAsync(CurrentUserId, requesterId);
        if (!success) return BadRequest("Cannot accept friend request.");
        return Ok(new { message = "Friend request accepted." });
    }

    [Authorize]
    [HttpPost("decline/{requesterId}")]
    public async Task<IActionResult> DeclineRequest(Guid requesterId)
    {
        var success = await _friendService.DeclineRequestAsync(CurrentUserId, requesterId);
        if (!success) return BadRequest("Cannot decline friend request.");
        return Ok(new { message = "Friend request declined." });
    }

    [Authorize]
    [HttpGet("relationship/{targetUserId}")]
    public async Task<IActionResult> GetRelationship(Guid targetUserId)
    {
        var relationship = await _friendService.GetRelationshipAsync(CurrentUserId, targetUserId);
        if (relationship == null)
        {
            return Ok(new
            {
                status = (FriendStatus?)null,
                outgoing = false
            });
        }

        return Ok(new
        {
            status = relationship.Status,
            outgoing = relationship.SenderId == CurrentUserId
        });
    }

    [Authorize]
    [HttpDelete("{targetUserId}")]
    public async Task<IActionResult> RemoveFriend(Guid targetUserId)
    {
        var success = await _friendService.RemoveFriendAsync(CurrentUserId, targetUserId);
        if (!success) return BadRequest("Cannot remove friend.");
        return Ok(new { message = "Friend removed." });
    }

    [Authorize]
    [HttpPost("block/{targetUserId}")]
    public async Task<IActionResult> BlockUser(string targetUserId)
    {
        var success = await _friendService.BlockUserAsync(CurrentUserId, Guid.Parse(targetUserId));
        if (!success) return BadRequest("Cannot block user.");
        return Ok(new { message = "User blocked." });
    }

    [Authorize]
    [HttpPost("unblock/{targetUserId}")]

    public async Task<IActionResult> UnblockUser(string targetUserId)
    {
        var result = await _friendService.UnblockUserAsync(CurrentUserId, Guid.Parse(targetUserId));
        if (!result)
            return BadRequest("User is not blocked or does not exist.");

        return Ok(new { message = "User unblocked successfully." });
    }


    [Authorize]
    [HttpGet("blocked-users")]
    public async Task<IActionResult> GetBlockedUsers()
    {
        // Get all friend requests where the current user is the recipient and status is Pending
        Console.WriteLine($"CurrentUserId: {CurrentUserId}");
        List<FriendDTO> blockedUsers = await _friendService.GetBlockedAsync(CurrentUserId);
        Console.WriteLine($"{blockedUsers}");

        return Ok(blockedUsers);
    }

    [Authorize]
    [HttpGet("blocked/{userId}")]
    public async Task<IActionResult> GetIsBlockedUser(string userId)
    {
        // Get all friend requests where the current user is the recipient and status is Pending
        Console.WriteLine($"CurrentUserId: {CurrentUserId}");
        var result = await _friendService.GetIsBlockedAsync(CurrentUserId, Guid.Parse(userId));
        if (!result.blocked)
            return Ok(new { message = "User is not blocked.", blocked = false});

        //Console.WriteLine($"{blockedUsers}");

        return Ok(new {message = "User Blocked", blocked = true});
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        List<FriendDTO> friends = await _friendService.GetFriendsAsync(CurrentUserId);
        return Ok(friends);
    }
    
    [Authorize]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingRequests()
    {
        // Get all friend requests where the current user is the recipient and status is Pending
        Console.WriteLine($"CurrentUserId: {CurrentUserId}");
        List<FriendDTO> pendingRequests = await _friendService.GetPendingRequestsAsync(CurrentUserId);
        Console.WriteLine($"Pending requests count: {pendingRequests.Count}");

        return Ok(pendingRequests);
    }
}
