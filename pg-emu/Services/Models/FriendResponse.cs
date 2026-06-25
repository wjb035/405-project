using Godot;
using System;
namespace PGEmu.Services.Models;

public partial class FriendResponse
{
	public string Id { get; set; }
	public string Username { get; set; } = string.Empty;
	public FriendStatus Status { get; set; }
}

public enum FriendStatus
{
		Pending,
		Accepted,
		Blocked
}
