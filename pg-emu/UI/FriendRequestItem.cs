using Godot;
using System;
using PGEmu.Services;
using PGEmu.UI;
using System.Threading.Tasks;

public partial class FriendRequestItem : InboxItem
{
	[Export] private Label UsernameLabel;
	[Export] private Button AcceptButton;
	[Export] private Button DeclineButton;

	private string userId;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		AcceptButton.Pressed += OnAccept;
		DeclineButton.Pressed += OnDecline;
	}
	
	
	// Gets the userID and username
	public override void Setup(object data)
	{
		if (data is FriendRequestDto dto)
		{
			userId = dto.Id;
			UsernameLabel.Text = dto.Username;
		}
	}

  private async void OnAccept()
	{
		AcceptButton.Disabled = true;
		DeclineButton.Disabled = true;

		// var success = await FriendService.Instance.RespondToRequest(userId, true);
		var success = await FakeRespondToRequest(); // test call
		
		if (success)
		{
			// remove from UI
			var tween = GetTree().CreateTween();
			tween.TweenProperty(this, "modulate:a", 0f, 0.2f).From(1f);
			tween.TweenCallback(Callable.From(() => QueueFree())); 
		}
		else
		{
			GD.PrintErr("Failed to accept request");
			ShowErrorPopup("Could not accept friend request");
			AcceptButton.Disabled = false;
			DeclineButton.Disabled = false;
		}
	}

	private async void OnDecline()
	{
		AcceptButton.Disabled = true;
		DeclineButton.Disabled = true;

		//  var success = await FriendService.Instance.RespondToRequest(userId, false);
		var success = await FakeRespondToRequest(); // test call
		
		if (success)
		{
			// remove from UI
			var tween = GetTree().CreateTween();
			tween.TweenProperty(this, "modulate:a", 0f, 0.2f).From(1f);
			tween.TweenCallback(Callable.From(() => QueueFree())); 
		}
		else
		{
			GD.PrintErr("Failed to decline request");
			AcceptButton.Disabled = false;
			DeclineButton.Disabled = false;
			ShowErrorPopup("Could not decline friend request");
		}
	}
	private void ShowErrorPopup(string message)
	{
		var popup = new AcceptDialog();
		popup.DialogText = message;
		AddChild(popup);
		popup.PopupCentered();
	}
	
	private async Task<bool> FakeRespondToRequest()
	{
		await Task.Delay(200); // simulate network delay
		return true; // simulate success
	}
}
