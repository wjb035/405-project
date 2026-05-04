using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using PGEmu.Services;


public partial class FriendInbox : PopupPanel
{
	[Export] private VBoxContainer InboxList;
	[Export] private Label StatusLabel;
	[Export] private PackedScene FriendRequestItemScene;
	[Export] private PackedScene MissedMessageItemScene;
	[Export] private Label MissedMessagesLabel;
	[Export] private Panel PopupContent;
	
	private ChatManager _chat;
	private List<FriendRequestDto> _lastRequests = new();
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Hide();
		
		_chat = GetNode<ChatManager>("/root/ChatManager");
		
		_chat.UnreadCountUpdated += OnUnreadCountUpdated;
		// Load requests when shown
	}
	
	public void ShowPopup()
	{
		AudioManager.Instance?.PlayMessageOpen();
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
		
		// remove existing children
		foreach (Node child in InboxList.GetChildren().ToArray())
		{
			child.QueueFree();
		} 

		// await ToSignal(GetTree().CreateTimer(1.0), "timeout"); // simulate delay

		_chat.LoadUnreadCounts();
		_chat.LoadUnreadMessages();
		
		List<FriendRequestDto> requests;
		try
		{
			requests = await FriendService.Instance.GetPendingRequests();
			/* requests = new List<FriendRequestDto>
			{
				new FriendRequestDto { Id = "1", Username = "Peter Scully" },
				new FriendRequestDto { Id = "2", Username = "Shabbibble" },
				new FriendRequestDto { Id = "3", Username = "Shiashdo" }
			}; */
		}
		catch (System.Exception ex)
		{
			GD.PrintErr(ex.Message);
			StatusLabel.Text = "Failed to load requests. Retry later.";
			StatusLabel.Visible = true;
			return;
		}
		
		GD.Print($"Friend requests loaded: {requests.Count}");
		foreach (var r in requests)
			GD.Print($"Request from: {r.Username}, Id: {r.Id}");
		_lastRequests = requests;
		RenderInbox();
		
	}
	private void OnUnreadCountUpdated()
	{
		RenderInbox();
	}
	
	private void RenderInbox()
	{
		foreach (Node child in InboxList.GetChildren())
			child.QueueFree();

		var unread = _chat.GetUnreadCounts();

		bool hasRequests = _lastRequests != null && _lastRequests.Count > 0;
		bool hasUnread = unread != null && unread.Count > 0;

		// EMPTY STATE FIRST 
		if (!hasRequests && !hasUnread)
		{
			StatusLabel.Text = "No notifications";
			StatusLabel.Visible = true;
			return;
		}

		StatusLabel.Visible = false;

		// UNREAD SECTION
		if (hasUnread)
		{
			foreach (var kvp in unread)
			{
				var item = MissedMessageItemScene.Instantiate<MissedMessageItem>();
				item.Setup(kvp.Key, "New messages", "", kvp.Value);
				InboxList.AddChild(item);
			}
		}

		// REQUEST SECTION
		if (hasRequests)
		{
			var header = new Label();
			header.Text = "Friend Requests";
			UiStyle.StyleMetaLabel(header);
			InboxList.AddChild(header);

			foreach (var req in _lastRequests)
			{
				var item = FriendRequestItemScene.Instantiate<FriendRequestItem>();
				item.Setup(req);
				InboxList.AddChild(item);
			}
		}
	}
	
}
