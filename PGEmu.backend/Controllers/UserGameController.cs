using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using PGEmuBackend.DTOs.UserGames;
using PGEmuBackend.Services;
namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/usergames")]
public class UserGameController : ControllerBase
{
    private readonly IUserGameService _userGameService;

    public UserGameController(IUserGameService userGameService)
    {
        _userGameService = userGameService;
    }

    // Helper to get current user
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirst("sub")?.Value ?? throw new Exception("User not authenticated"));

    // ========================
    // GET: Get all user games
    // ========================
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetMyGames()
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        

        // Return them as JSON
        return Ok();
    }

    // ========================
    // POST: Add a game
    // ========================
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> AddGame([FromBody] object request)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        // TODO: call service to add game
        return Ok();
    }

    // ========================
    // PUT: Update playtime
    // ========================
    
    [Authorize]
    [HttpPut("playtime")]
    public async Task<IActionResult> UpdatePlaytime([FromBody] UpdatePlaytimeDTO request)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        var (gameName, playtimeMinutes, platformId) = await _userGameService.UpdatePlaytimeAsync(
            Guid.Parse(userIdClaim.Value), 
            request
        );

        return Ok(new
        {
            gameName,
            playtimeMinutes,
            platformId
        });
    }

    // ========================
    // PUT: Toggle favorite
    // ========================
    [Authorize]
    [HttpPut("{id}/favorite")]
    public async Task<IActionResult> ToggleFavorite(int id)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        // TODO: call service to toggle favorite
        return Ok();
    }

    // ========================
    // DELETE: Remove a game
    // ========================
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> RemoveGame(int id)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub);
        if (userIdClaim == null)
            return BadRequest("User not authenticated.");

        // TODO: call service to delete game
        return Ok();
    }
}