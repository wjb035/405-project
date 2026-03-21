using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PGEmuBackend.DTOs.Social;
using PGEmuBackend.Models;
using PGEmuBackend.Services;

namespace PGEmuBackend.Controllers;
[ApiController]
[Route("api/activity")]
public class ActivityController : ControllerBase
{
    private readonly IUserActivityService _activityService;

    public ActivityController(IUserActivityService activityService)
    {
        _activityService = activityService;
    }

    // ✅ SET activity
    [HttpPost("set")]
    public async Task<IActionResult> SetActivity([FromBody] SetActivityDto dto)
    {
        await _activityService.SetActivityAsync(
            dto.UserId,
            dto.ActivityType,
            dto.ExternalGameId,
            dto.Source
        );

        return Ok();
    }

    // ✅ GET activity
    [HttpGet("{userId}")]
    public async Task<ActionResult<UserActivity>> GetActivity(Guid userId)
    {
        var activity = await _activityService.GetCurrentActivityAsync(userId);

        if (activity == null)
            return NotFound();

        return Ok(activity);
    }
}