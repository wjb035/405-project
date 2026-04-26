using Godot;
using System;

public partial class ChatTesting : Control
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		var chatManager = GetNode<ChatManager>("/root/ChatManager");
		chatManager.Username = "TestUser";
		chatManager.ConnectToChat();
		chatManager.DmReceived += (from, msg, time) =>
			GD.Print($"DM from {from}: {msg}");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
