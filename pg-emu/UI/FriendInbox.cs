using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

using PGEmu.Services;


public partial class FriendInbox : Panel
{
	[Export] private VBoxContainer InboxList;
	[Export] public PackedScene FriendRequestItemScene = null;
	[Export] private Label StatusLabel;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		LoadFriendRequests();
	}
	
	private async void LoadFriendRequests()
	{
		// Handles the loading of the friend requests from the database
		StatusLabel.Text = "Loading...";
		StatusLabel.Visible = true;

		List<FriendRequestDto> requests = null;
		
		try
		{
			requests = await FriendService.Instance.GetPendingRequests();
		}
		catch
		{
			StatusLabel.Text = "Failed to load requests. Retry later.";
			return;
		}
		
		// remove existing children
		foreach (Node child in InboxList.GetChildren())
		{
			child.QueueFree();
		} 
		
		// If there arent requests, display there are none
		if (requests == null || requests.Count == 0)
		{
			StatusLabel.Text = "No requests";
			return;
		}
		
		StatusLabel.Visible = false;

		// If there are requests, display them
		foreach (var req in requests)
		{
			var item = FriendRequestItemScene.Instantiate<FriendRequestItem>();
			InboxList.AddChild(item);
			item.Setup(req);
		}
	}
}
