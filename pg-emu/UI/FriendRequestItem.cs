using Godot;
using System;
using PGEmu.Services;
using PGEmu.UI;
using System.Threading.Tasks;

namespace PGEmu.UI;

public partial class FriendRequestItem : InboxItem
{
	private Label UsernameLabel;
	private Button AcceptButton;
	private Button DeclineButton;

	private string userId;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		UsernameLabel = GetNode<Label>("HBox/UsernameLabel");
		AcceptButton = GetNode<Button>("HBox/AcceptButton");
		DeclineButton = GetNode<Button>("HBox/DeclineButton");

		AcceptButton.Pressed += OnAccept;
		DeclineButton.Pressed += OnDecline;
		
		ApplyAesthetic();
	}
	
	
	// Gets the userID and username
	public override void Setup(object data)
	{
		UsernameLabel ??= GetNode<Label>("HBox/UsernameLabel");
		AcceptButton ??= GetNode<Button>("HBox/AcceptButton");
		DeclineButton ??= GetNode<Button>("HBox/DeclineButton");
		
		if (data is FriendRequestDto dto)
		{
			userId = dto.Id;
			UsernameLabel.Text = dto.Username;
		}
		GD.Print($"Missed items created in UI");
	}

  private async void OnAccept()
	{
		AudioManager.Instance?.PlaySelect();
		AcceptButton.Disabled = true;
		DeclineButton.Disabled = true;

		var success = await FriendService.Instance.RespondToRequest(userId, true);
		// var success = await FakeRespondToRequest(); // test call
		
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
		AudioManager.Instance?.PlayClick();
		AcceptButton.Disabled = true;
		DeclineButton.Disabled = true;

		var success = await FriendService.Instance.RespondToRequest(userId, false);
		// var success = await FakeRespondToRequest(); // test call
		
		if (success)
		{
			// remove from UI
			var tween = GetTree().CreateTween();
			tween.TweenProperty(this, "modulate:a", 0f, 0.2f).From(1f);
			FriendInbox.Instance?.RemoveRequest(userId);
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

	private void ApplyAesthetic()
	{
		// Match game selection controls to the same launcher palette and contrast rules.
		// Nav buttons
		UiStyle.AddHoverFeedback(AcceptButton);
		
		UiStyle.AddHoverFeedback(DeclineButton);

		UiStyle.StyleTitleLabel(UsernameLabel);

	}

}
