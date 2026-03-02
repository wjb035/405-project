using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PGEmuBackend.DTOs.ProfileCustomization;
using System.IdentityModel.Tokens.Jwt;
using PGEmuBackend.Services;
using PGEmuBackend.Models;
using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileCustomizationController : ControllerBase
{
    private readonly IProfileCustomizationService _profileCustomizationService;

    public ProfileCustomizationController(IProfileCustomizationService profileCustomizationService)
    {
        _profileCustomizationService = profileCustomizationService;
    }

    // Helper to get current user
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirst("sub")?.Value ?? throw new Exception("User not authenticated"));


    //HTTP Endpoints for profile actions
    //[Authorize]
    //[HttpPost("setDisplayName/{newDisplayName}")]
    //public async Task<IActionResult> SetDisplayName(string newDisplayName)
    //{
    //    var success = await _profileCustomizationService.SetDisplayName(CurrentUserId, newDisplayName);
    //    if (!success) return BadRequest("Cannot change display name.");
    //    return Ok(new { message = "Display name changed." });
    //}

    //[Authorize]
    //[HttpPost("setBio/{newBio}")]
    //public async Task<IActionResult> SetBio(string newBio)
    //{
    //    var success = await _profileCustomizationService.SetBio(CurrentUserId, newBio);
    //    if (!success) return BadRequest("Cannot change bio.");
    //    return Ok(new { message = "Bio name changed." });
    //}

    [Authorize]
    [HttpPut("username")]
    public async Task<IActionResult> ChangeUsername([FromBody] ProfileCustomizationDTO request)
    {
        // authorize user
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");


        var result = await _profileCustomizationService.ChangeUsernameAsync(Guid.Parse(userIdClaim.Value), request.NewUsername);
        if (!result.Success)
            return BadRequest(result.Message);

        return Ok(new { message = result.Message, Username = result.NewUsername });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyUsername()
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        var user = await _profileCustomizationService.GetUserAsync(Guid.Parse(userIdClaim.Value));
        if (!user.Success)
            return NotFound("Profile not found.");

        return Ok(new { Message = user.Message, Username = user.Profile.Username, Bio = user.Profile.Bio, AvatarUrl = user.Profile.AvatarUrl});
    }

    [Authorize]
    [HttpPut("bio")]
    public async Task<IActionResult> ChangeBio([FromBody] ProfileCustomizationDTO request)
    {
        // authorize user
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");


        var result = await _profileCustomizationService.ChangeBioAsync(Guid.Parse(userIdClaim.Value), request.NewBio);
        if (!result.Success)
            return BadRequest(result.Message);

        return Ok(new { message = result.Message, Bio = result.NewBio });
    }

    [Authorize]
    [HttpPut("avatar")]
    public async Task<IActionResult> ChangeAvatar([FromBody] ProfileCustomizationDTO request)
    {
        // authorize user
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");


        var result = await _profileCustomizationService.ChangeAvatarAsync(Guid.Parse(userIdClaim.Value), request.NewAvatarUrl);
        if (!result.Success)
            return BadRequest(result.Message);

        return Ok(new { message = result.Message, Avatar = result.NewAvatarUrl });
    }




    //[Authorize]
    //[HttpGet]
    //public async Task<IActionResult> ()
    //{
    //    List<FriendDTO> friends = await _friendService.GetFriendsAsync(CurrentUserId);
    //    return Ok(friends);
    //}

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


    //    

    //    [Authorize]
    //    [HttpGet("pending")]
    //    public async Task<IActionResult> GetPendingRequests()
    //    {
    //        // Get all friend requests where the current user is the recipient and status is Pending
    //        List<FriendDTO> pendingRequests = await _friendService.GetPendingRequestsAsync(CurrentUserId);

    //        return Ok(pendingRequests);
    //    }
}
