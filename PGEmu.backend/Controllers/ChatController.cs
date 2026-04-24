using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;

namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _db;
    public ChatController(AppDbContext db) => _db = db;
    
    // Direct message history storage, you have to load more after 50 messages
    [HttpGet("dm")]
    public async Task<IActionResult> GetDMHistory(
        string user1, string user2,
        DateTime? before = null, int limit = 50)
    {
        var query = _db.ChatMessages
            .Where(m => !m.IsGroupMessage &&
                        ((m.FromUser == user1 && m.ToUser == user2) ||
                         (m.FromUser == user2 && m.ToUser == user1)));

        if (before.HasValue)
            query = query.Where(m => m.SentAt < before.Value);

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        return Ok(messages);
    }

    
    // Same thing for group chats
    [HttpGet("group")]
    public async Task<IActionResult> GetGroupHistory(
        string groupId,
        DateTime? before = null, int limit = 50)
    {
        var query = _db.ChatMessages
            .Where(m => m.IsGroupMessage && m.GroupId == groupId);

        if (before.HasValue)
            query = query.Where(m => m.SentAt < before.Value);

        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        return Ok(messages);
    }
}