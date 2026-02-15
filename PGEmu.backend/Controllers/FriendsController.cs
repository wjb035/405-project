using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PGEmuBackend.DTOs.Social;
using PGEmuBackend.Services;

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
    [HttpPost("block/{targetUserId}")]
    public async Task<IActionResult> BlockUser(Guid targetUserId)
    {
        var success = await _friendService.BlockUserAsync(CurrentUserId, targetUserId);
        if (!success) return BadRequest("Cannot block user.");
        return Ok(new { message = "User blocked." });
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
        List<FriendDTO> pendingRequests = await _friendService.GetPendingRequestsAsync(CurrentUserId);

        return Ok(pendingRequests);
    }
}
