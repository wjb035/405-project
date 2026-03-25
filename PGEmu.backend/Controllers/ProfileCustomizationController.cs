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
    [HttpGet("{username}")]
    public async Task<IActionResult> GetUserProfile(string username)
    {
        var user = await _profileCustomizationService.GetUserAsync(null, username);
        if (!user.Success)
            return NotFound("Profile not found.");

        return Ok(new { Message = user.Message, Username = user.Profile.Username, Bio = user.Profile.Bio, AvatarUrl = user.Profile.AvatarUrl });
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

}
