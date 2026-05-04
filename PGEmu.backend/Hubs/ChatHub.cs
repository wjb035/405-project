using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Data;
using PGEmuBackend.Models;

namespace PGEmuBackend.Hubs;

public class ChatHub : Hub
{
     
     private readonly AppDbContext _db;

     public ChatHub(AppDbContext db)
     {
          _db = db;
     }
     
     // Shared dictionary: username to connectionId, group id to group info
     private static readonly ConcurrentDictionary<string, string> _users = new();
     private static readonly ConcurrentDictionary<string, GroupInfo> _groups = new();
     
     public class GroupInfo
     {
          public string Name { get; set; } = "";
          public string Owner { get; set; } = "";
     }
     
     // CONNECTION SHIT
     // Handles connection and being added back to group chats the user was arleady in
     public async Task Register(string username)
     {
          _users[username] = Context.ConnectionId;
          
          var userGroups = await _db.ChatGroupMembers
               .Where(m => m.Username == username)
               .Select(m => m.GroupId)
               .ToListAsync();

          foreach (var groupId in userGroups)
               await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
          
          await Clients.Others.SendAsync("UserOnline", username);
     }
    
     // Handles disconnection
     public override async Task OnDisconnectedAsync(Exception? exception)
     {
          var user = _users.FirstOrDefault(x => x.Value == Context.ConnectionId);
          if (user.Key != null)
          {
               _users.TryRemove(user.Key, out _);
               await Clients.Others.SendAsync("UserOffline", user.Key);
          }
          await base.OnDisconnectedAsync(exception);
     }
    
     // DMS
     public async Task SendDM(string fromUser, string toUser, string message)
     {
          // Save to DB first
          var chat = new ChatMessage
          {
               FromUser = fromUser,
               ToUser = toUser,
               Content = message,
               SentAt = DateTime.UtcNow,
               IsGroupMessage = false
          };
          _db.ChatMessages.Add(chat);
          await _db.SaveChangesAsync();
          
          if (_users.TryGetValue(toUser, out var recipientConnectionId))
          {
               await Clients.Client(recipientConnectionId).SendAsync("ReceiveDirectMessage", fromUser, message, chat.SentAt);
          }
          await Clients.Caller.SendAsync("ReceiveDirectMessage", fromUser, message, chat.SentAt);
     }
     

     // GROUOP SHIT
     public async Task CreateGroup(string owner, string groupName)
     {
          // Saves to db
          var group = new ChatGroup
          {
               Id = Guid.NewGuid().ToString(),
               Name = groupName,
               Owner = owner,
               Members = new List<ChatGroupMember>
               {
                    new ChatGroupMember { Username = owner }
               }
          };
          _db.ChatGroups.Add(group);
          await _db.SaveChangesAsync();
          
          // Track in memory for live routing
          _groups[group.Id] = new GroupInfo { Name = groupName, Owner = owner };
          await Groups.AddToGroupAsync(Context.ConnectionId, group.Id);

          await Clients.Caller.SendAsync("GroupCreated", group.Id, groupName);
          await Clients.Caller.SendAsync("ReceiveGroupMessage", group.Id, "System",
               $"Group \"{groupName}\" created");
     }

     public async Task InviteToGroup(string inviter, string groupId, string targetUser)
     {   
          var group = _groups[groupId];
          if (_users.TryGetValue(targetUser, out var targetConnectionId))
               await Clients.Client(targetConnectionId)
                    .SendAsync("GroupInviteReceived", groupId, group.Name, inviter);
          else
               await Clients.Caller.SendAsync("Error", $"{targetUser} is not online");
     }
     
     public async Task AcceptGroupInvite(string username, string groupId)
     {
          // Saves to db
          _db.ChatGroupMembers.Add(new ChatGroupMember
          {
               GroupId = groupId,
               Username = username
          });
          await _db.SaveChangesAsync();
          
          // Loads group info
          var group = await _db.ChatGroups
               .Include(g => g.Members)
               .FirstAsync(g => g.Id == groupId);
          
          // Adds to signalR group
          await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
          
          var memberNames = group.Members.Select(m => m.Username).ToList();
          await Clients.Caller.SendAsync("GroupJoined", groupId, group.Name, memberNames);
          await Clients.OthersInGroup(groupId).SendAsync("ReceiveGroupMessage", groupId,
               "System", $"{username} joined the group");
          await Clients.OthersInGroup(groupId).SendAsync("GroupMemberJoined", groupId, username);
     }
     

     public async Task DeclineGroupInvite(string username, string groupId)
     {
          var group = _groups[groupId];
          if (_users.TryGetValue(group.Owner, out var ownerConnectionId))
               await Clients.Client(ownerConnectionId)
                    .SendAsync("Error", $"{username} declined the group invite");
     }
     
     public async Task LeaveGroup(string username, string groupId)
     {
          // Remove from DB
          var member = await _db.ChatGroupMembers
               .FirstAsync(m => m.GroupId == groupId && m.Username == username);
          _db.ChatGroupMembers.Remove(member);
          await _db.SaveChangesAsync();
          
          // Remove from SignalR
          await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
          await Clients.Caller.SendAsync("GroupLeft", groupId);
          await Clients.Group(groupId).SendAsync("ReceiveGroupMessage", groupId,
               "System", $"{username} left the group");
          await Clients.Group(groupId).SendAsync("GroupMemberLeft", groupId, username);
          
          // Transfer ownership
          var group = await _db.ChatGroups.Include(g => g.Members)
               .FirstAsync(g => g.Id == groupId);

          if (group.Owner == username)
          {
               if (group.Members.Any())
               {
                    group.Owner = group.Members.First().Username;
                    await _db.SaveChangesAsync();
                    await Clients.Group(groupId).SendAsync("ReceiveGroupMessage", groupId,
                         "System", $"{group.Owner} is now the group owner");
               }
               else
               {
                    // If there aren't any mmebers left, just delete it
                    _db.ChatGroups.Remove(group);
                    await _db.SaveChangesAsync();
                    _groups.TryRemove(groupId, out _);
               }
          }
     }
     
     public async Task RemoveFromGroup(string owner, string groupId, string targetUser)
     {
          var group = _groups[groupId];

          // Only owner can kick people
          if (group.Owner != owner)
          {
               await Clients.Caller.SendAsync("Error", "Only the group owner can remove members");
               return;
          }

          // Remove from DB
          var member = await _db.ChatGroupMembers
               .FirstAsync(m => m.GroupId == groupId && m.Username == targetUser);
          _db.ChatGroupMembers.Remove(member);
          await _db.SaveChangesAsync();
          
          if (_users.TryGetValue(targetUser, out var targetConnectionId))
          {
               await Groups.RemoveFromGroupAsync(targetConnectionId, groupId);
               await Clients.Client(targetConnectionId).SendAsync("GroupLeft", groupId);
          }
          await Clients.Group(groupId).SendAsync("ReceiveGroupMessage", groupId,
               "System", $"{targetUser} was removed from the group");
          await Clients.Group(groupId).SendAsync("GroupMemberLeft", groupId, targetUser);
     }
     
     
     public async Task SendGroupMessage(string username, string groupId, string message)
     {
          var chat = new ChatMessage
          {
               FromUser = username,
               GroupId = groupId,
               Content = message,
               SentAt = DateTime.UtcNow,
               IsGroupMessage = true
          };
          _db.ChatMessages.Add(chat);
          await _db.SaveChangesAsync();

          await Clients.Group(groupId)
               .SendAsync("ReceiveGroupMessage", groupId, username, message, chat.SentAt);
     }
}