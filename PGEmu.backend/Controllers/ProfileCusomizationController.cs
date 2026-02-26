using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PGEmuBackend.DTOs.ProfileCustomization;
using PGEmuBackend.Services;

namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileCustomizationController : ControllerBase
{
    private readonly ProfileCustomizationService _profileCustomizationService;

    public ProfileCustomizationController(ProfileCustomizationService profileCustomizationService)
    {
        _profileCustomizationService = profileCustomizationService;
    }

    // Helper to get current user
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirst("sub")?.Value ?? throw new Exception("User not authenticated"));


    //HTTP Endpoints for friend actions
    [Authorize]
    [HttpPost("setDisplayName/{newDisplayName}")]
    public async Task<IActionResult> SendRequest(string newDisplayName)
    {
        var success = await _profileCustomizationService.SetDisplayName(CurrentUserId, newDisplayName);
        if (!success) return BadRequest("Cannot change display name.");
        return Ok(new { message = "Display name changed." });
    }

//    [Authorize]
//    [HttpPost("accept/{requesterId}")]
//    public async Task<IActionResult> AcceptRequest(Guid requesterId)
//    {
//        var success = await _friendService.AcceptRequestAsync(CurrentUserId, requesterId);
//        if (!success) return BadRequest("Cannot accept friend request.");
//        return Ok(new { message = "Friend request accepted." });
//    }

//    [Authorize]
//    [HttpPost("decline/{requesterId}")]
//    public async Task<IActionResult> DeclineRequest(Guid requesterId)
//    {
//        var success = await _friendService.DeclineRequestAsync(CurrentUserId, requesterId);
//        if (!success) return BadRequest("Cannot decline friend request.");
//        return Ok(new { message = "Friend request declined." });
//    }

//    [Authorize]
//    [HttpPost("block/{targetUserId}")]
//    public async Task<IActionResult> BlockUser(Guid targetUserId)
//    {
//        var success = await _friendService.BlockUserAsync(CurrentUserId, targetUserId);
//        if (!success) return BadRequest("Cannot block user.");
//        return Ok(new { message = "User blocked." });
//    }

//    [Authorize]
//    [HttpPost("unblock/{targetUserId}")]

//    public async Task<IActionResult> UnblockUser(Guid targetUserId)
//    {
//        var result = await _friendService.UnblockUserAsync(CurrentUserId, targetUserId);
//        if (!result)
//            return BadRequest("User is not blocked or does not exist.");

//        return Ok(new { message = "User unblocked successfully." });
//    }


//    [Authorize]
//    [HttpGet]
//    public async Task<IActionResult> GetFriends()
//    {
//        List<FriendDTO> friends = await _friendService.GetFriendsAsync(CurrentUserId);
//        return Ok(friends);
//    }

//    [Authorize]
//    [HttpGet("pending")]
//    public async Task<IActionResult> GetPendingRequests()
//    {
//        // Get all friend requests where the current user is the recipient and status is Pending
//        List<FriendDTO> pendingRequests = await _friendService.GetPendingRequestsAsync(CurrentUserId);

//        return Ok(pendingRequests);
//    }
}
