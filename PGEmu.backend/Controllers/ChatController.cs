using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;
using PGEmuBackend.DTOs.Social;
using PGEmuBackend.Models;

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
    [HttpGet("unread")]
    public async Task<IActionResult> GetUnread(string user)
    {
        var states = await _db.ChatReadStates
            .Where(s => s.Username == user)
            .ToListAsync();

        var messages = await _db.ChatMessages
            .Where(m => m.ToUser == user)
            .OrderByDescending(m => m.SentAt)
            .ToListAsync();

        var result = messages
            .Where(m =>
            {
                var state = states.FirstOrDefault(s => s.OtherUser == m.FromUser);
                return m.SentAt > (state?.LastReadAt ?? DateTime.MinValue);
            })
            .OrderByDescending(m => m.SentAt)
            .ToList();

        return Ok(result);
    }
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCounts(string user)
    {
        var states = await _db.ChatReadStates
            .Where(s => s.Username == user)
            .ToListAsync();

        var messages = await _db.ChatMessages
            .Where(m => m.ToUser == user)
            .ToListAsync();

        var result = messages
            .GroupBy(m => m.FromUser)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var state = states.FirstOrDefault(s => s.OtherUser == g.Key);
                    var lastRead = state?.LastReadAt ?? DateTime.MinValue;

                    return g.Count(m => m.SentAt > lastRead);
                }
            );

        return Ok(result);
    }
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead([FromBody] MarkReadDTO dto)
    {
        var state = await _db.ChatReadStates
            .FirstOrDefaultAsync(s =>
                s.Username == dto.Username &&
                s.OtherUser == dto.OtherUser);

        if (state == null)
        {
            state = new ChatReadState
            {
                Username = dto.Username,
                OtherUser = dto.OtherUser,
                LastReadAt = DateTime.UtcNow
            };

            _db.ChatReadStates.Add(state);
        }
        else
        {
            state.LastReadAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok();
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