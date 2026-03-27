using Godot;
using System;
namespace PGEmu.Services.Models;

public class ProfileResponse
{
	public string Message { get; set; }
	public string UserId { get; set; }
	public string Username { get; set; }
	public string Bio { get; set; }
	public string AvatarUrl { get; set; }
	
}
