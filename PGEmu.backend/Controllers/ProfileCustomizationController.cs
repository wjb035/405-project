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
    private readonly AvatarService _avatarService;
    
    public ProfileCustomizationController(IProfileCustomizationService profileCustomizationService,
        AvatarService avatarService)
    {
        _profileCustomizationService = profileCustomizationService;
        _avatarService = avatarService;
    }

    // Helper to get current user
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirst("sub")?.Value ?? throw new Exception("User not authenticated"));


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

        var user = await _profileCustomizationService.GetUserAsync(Guid.Parse(userIdClaim.Value), null);
        if (!user.Success)
            return NotFound("Profile not found.");

        return Ok(new { Message = user.Message, Username = user.Profile.Username, Bio = user.Profile.Bio, AvatarUrl = user.Profile.AvatarUrl });
    }

    [Authorize]
    [HttpGet("search")]
    public async Task<IActionResult> SearchUsersBySimilarity([FromQuery] string query, [FromQuery] int limit = 12)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        if (string.IsNullOrWhiteSpace(query))
            return Ok(Array.Empty<UserSearchResultDTO>());

        var currentUserId = Guid.Parse(userIdClaim.Value);
        var searchResults = await _profileCustomizationService.SearchUsersBySimilarityAsync(currentUserId, query, limit);
        return Ok(searchResults);
    }

    [Authorize]
    [HttpGet("{username}/friends")]
    public async Task<IActionResult> GetUserFriends(string username, [FromQuery] int limit = 12)
    {
        var friends = await _profileCustomizationService.GetUserFriendsAsync(username, limit);
        return Ok(friends);
    }

    [Authorize]
    [HttpGet("{username}/games")]
    public async Task<IActionResult> GetUserRecentGames(string username, [FromQuery] int limit = 8)
    {
        var games = await _profileCustomizationService.GetUserRecentGamesAsync(username, limit);
        return Ok(games);
    }


    [Authorize]
    [HttpGet("{username}")]
    public async Task<IActionResult> GetUserProfile(string username)
    {
        var user = await _profileCustomizationService.GetUserAsync(null, username);
        if (!user.Success)
            return NotFound("Profile not found.");

        return Ok(new { Message = user.Message, UserId = user.Profile.UserId, Username = user.Profile.Username, Bio = user.Profile.Bio, AvatarUrl = user.Profile.AvatarUrl });
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
    [HttpPost("avatar")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> ChangeAvatar(IFormFile file)
    {
        // Checck auth
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        var userId = Guid.Parse(userIdClaim.Value);

        string avatarUrl;
        try
        {
            avatarUrl = await _avatarService.SaveAvatarAsync(userId, file);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var result = await _profileCustomizationService.ChangeAvatarAsync(userId, avatarUrl);
        if (!result.Success)
            return BadRequest(result.Message);

        return Ok(new { message = result.Message, Avatar = result.NewAvatarUrl });
    }
    
    [Authorize]
    [HttpDelete("avatar")]
    public async Task<IActionResult> DeleteAvatar()
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        var userId = Guid.Parse(userIdClaim.Value);

        _avatarService.DeleteAvatar(userId);

        var result = await _profileCustomizationService.ChangeAvatarAsync(userId, null);
        if (!result.Success)
            return BadRequest(result.Message);

        return NoContent();
    }

}
