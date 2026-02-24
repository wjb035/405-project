using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using PGEmu.Services;


public partial class FriendInbox : PopupPanel
{
	[Export] private VBoxContainer InboxList;
	[Export] private Label StatusLabel;
	[Export] private PackedScene FriendRequestItemScene ;
	[Export] private Panel PopupContent;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Hide();
		// Load requests when shown
	}
	
	public void ShowPopup()
	{
		GrabFocus();
		
		Position = new Vector2I(740,60);
		Popup();
		
		PopupContent.Scale = new Vector2(0.8f, 0.8f);
		PopupContent.Modulate = new Color(1,1,1,0);
		
		var tween = CreateTween();
		
		tween.TweenProperty(PopupContent, "scale", new Vector2(1f,1f), 0.1f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Back);
		
		tween.TweenProperty(PopupContent, "modulate:a", 1f, 0.1f);
		LoadFriendRequests();
	}

	public void HidePopup()
	{
		var tween = CreateTween();
		
		tween.TweenProperty(PopupContent, "scale", new Vector2(0.8f, 0.8f), 0.15f)
			.SetEase(Tween.EaseType.In)
			.SetTrans(Tween.TransitionType.Back);
		
		tween.TweenProperty(PopupContent, "modulate:a", 0f, 0.15f)
			.SetEase(Tween.EaseType.In);

		tween.Finished += () => Hide();
	}
	
	
	private async void LoadFriendRequests()
	{

		// Handles the loading of the friend requests from the database
		StatusLabel.Text = "Loading...";
		StatusLabel.Visible = true;

		await ToSignal(GetTree().CreateTimer(1.0), "timeout"); // simulate delay

		List<FriendRequestDto> requests;
		try
		{
			// requests = await FriendService.Instance.GetPendingRequests();
			requests = new List<FriendRequestDto>
			{
				new FriendRequestDto { Id = "1", Username = "Peter Scully" },
				new FriendRequestDto { Id = "2", Username = "Shabbibble" },
				new FriendRequestDto { Id = "3", Username = "Shiashdo" }
			};
		}
		catch (System.Exception ex)
		{
			GD.PrintErr(ex.Message);
			StatusLabel.Text = "Failed to load requests. Retry later.";
			return;
		}

		// remove existing children
		foreach (Node child in InboxList.GetChildren().ToArray())
		{
			child.QueueFree();
		} 
			
		// If there arent requests, display there are none
		if (requests == null || requests.Count == 0)
		{
			StatusLabel.Text = "No requests";
			StatusLabel.Visible = true;
			return;
		}
	
		StatusLabel.Visible = false;

		// If there are requests, display them
		foreach (var req in requests)
		{
			var item = FriendRequestItemScene.Instantiate<FriendRequestItem>();
			InboxList.AddChild(item);
			item.Setup(req);
			
			// animation
			item.Modulate = new Color(1,1,1,0);
			var tween = CreateTween();
			tween.TweenProperty(item, "modulate:a", 1f, 0.2f);
		}
	}
}
