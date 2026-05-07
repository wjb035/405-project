using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using PGEmu.Services;
namespace PGEmu.UI;

public partial class FriendInbox : PopupPanel
{
	[Export] private VBoxContainer InboxList;
	[Export] private Label StatusLabel;
	[Export] private PackedScene FriendRequestItemScene;
	[Export] private PackedScene MissedMessageItemScene;
	[Export] private Panel PopupContent;
	
	private ChatManager _chat;
	private List<FriendRequestDto> _lastRequests = new();
	private Dictionary<string, (string preview, string sentAt)> _unreadPreviews = new();
	private int _loadVersion = 0;
	public static FriendInbox Instance { get; private set; }
	
	// Inbox badge
	private Button _badgeButton;
	private Label _badgeLabel;
	private Control _badgeWrapper;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Hide();
		Instance = this;
		_chat = GetNode<ChatManager>("/root/ChatManager");
		
		
		_chat.UnreadCountUpdated += OnUnreadCountUpdated;
		_chat.UnreadMessagesLoaded += OnUnreadMessagesLoaded;
		
		// Load requests when shown
	}
	
	public override void _Input(InputEvent @event)
	{
		if (InputRoutingService.Instance?.IsUiInputBlocked == true)
			return;

		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			var focused = GetViewport().GuiGetFocusOwner();
			if (focused is LineEdit || focused is TextEdit)
				return;
			
			if (key.Keycode == Key.I)
				ShowPopup();
		}
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
		int version = ++_loadVersion;
		
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
		
		var requestsTask = FriendService.Instance.GetPendingRequests();
		var signalTask = ToSignal(_chat, ChatManager.SignalName.UnreadCountUpdated);
		
		try
		{
			_lastRequests = await requestsTask ?? new List<FriendRequestDto>();
			
		}
		catch (System.Exception ex)
		{
			GD.PrintErr(ex.Message);
			StatusLabel.Text = "Failed to load requests. Retry later.";
			StatusLabel.Visible = true;
			return;
		}
		
		await signalTask;
		
		if (!IsInsideTree() || version != _loadVersion)
			return;
		
		RenderInbox();
	}
	private void OnUnreadCountUpdated()
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
		UpdateBadge();
		RenderInbox();
	}
	
	private void OnUnreadMessagesLoaded(string json)
	{
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
		
		var messages = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    
		if (messages == null) return;
    
		_unreadPreviews.Clear();
		// Group by fromUser, keep most recent
		foreach (var msg in messages)
		{
			var fromUser = msg.TryGetValue("fromUser", out var f) ? f.GetString() ?? "" : "";
			var content = msg.TryGetValue("content", out var c) ? c.GetString() ?? "" : "";
			var sentAt = msg.TryGetValue("sentAt", out var s) ? s.GetString() ?? "" : "";
        
			if (!string.IsNullOrEmpty(fromUser) && !_unreadPreviews.ContainsKey(fromUser))
				_unreadPreviews[fromUser] = (content, sentAt);
		}
    
		RenderInbox();
	}
	
	private void RenderInbox()
	{
		if (!IsInsideTree())
			return;
		
		foreach (Node child in InboxList.GetChildren())
			child.QueueFree();

		var unread = _chat.GetUnreadCounts();

		bool hasRequests = _lastRequests != null && _lastRequests.Count > 0;
		bool hasUnread = unread != null && unread.Values.Any(v => v > 0);

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
			var header = new Label { Text = "Unread Messages" };
			UiStyle.StyleTitleLabel(header);
			InboxList.AddChild(header);
			
			foreach (var kvp in unread.Where(kvp => kvp.Value > 0))
			{
				var item = MissedMessageItemScene.Instantiate<MissedMessageItem>();
				var preview = _unreadPreviews.TryGetValue(kvp.Key, out var p) ? p.preview : "New message";
				var sentAt = _unreadPreviews.TryGetValue(kvp.Key, out var p2) ? p2.sentAt : "";
				item.Setup(kvp.Key, preview, sentAt, kvp.Value);
				InboxList.AddChild(item);

				item.Modulate = new Color(1, 1, 1, 0);
				var tween = CreateTween();
				tween.TweenProperty(item, "modulate:a", 1f, 0.2f);
			}
		}

		// REQUEST SECTION
		if (hasRequests)
		{
			var header = new Label();
			header.Text = "Friend Requests";
			UiStyle.StyleTitleLabel(header);
			InboxList.AddChild(header);

			foreach (var req in _lastRequests)
			{
				var item = FriendRequestItemScene.Instantiate<FriendRequestItem>();
				item.Setup(req);
				InboxList.AddChild(item);
			}
		}
	}
	
	// Notificaiton badge
	public void AttachBadgeButton(Button button)
	{
		_badgeButton = button;
		var badgeWrapper = new Control();
		badgeWrapper.AnchorLeft = 1f;
		badgeWrapper.AnchorTop = 0f;
		badgeWrapper.OffsetLeft = -18f;
		badgeWrapper.OffsetTop = 2f;
		badgeWrapper.Size = Vector2.Zero;
		badgeWrapper.MouseFilter = Control.MouseFilterEnum.Ignore;
		badgeWrapper.Visible = false;
		
		var badgeBg = new ColorRect();
		badgeBg.Color = new Color(1f, 0.1f, 0.35f, 1f);
		badgeBg.Size = new Vector2(14, 14);
		badgeBg.Position = new Vector2(0, 0);
		badgeBg.MouseFilter = Control.MouseFilterEnum.Ignore;
		
		var circleMat = new ShaderMaterial();
		circleMat.Shader = GD.Load<Shader>("res://ShaderSlop/circle.gdshader");
		circleMat.SetShaderParameter("stroke_width", 0.00f);
		circleMat.SetShaderParameter("edge_softness", 0.05f);
		badgeBg.Material = circleMat;
		
		var center = new CenterContainer();
		center.Size = badgeBg.Size;
		
		_badgeLabel = new Label();
		_badgeLabel.AddThemeColorOverride("font_color", Colors.White);
		_badgeLabel.AddThemeFontSizeOverride("font_size", 9);
		_badgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_badgeLabel.VerticalAlignment = VerticalAlignment.Center;
		_badgeLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		
		center.AddChild(_badgeLabel);
		
		badgeWrapper.AddChild(badgeBg);
		badgeWrapper.AddChild(center);
		button.AddChild(badgeWrapper);
		
		_badgeWrapper = badgeWrapper;
    
		UpdateBadge();
	}

	private void UpdateBadge()
	{
		if (_badgeWrapper == null || !GodotObject.IsInstanceValid(_badgeWrapper)) return;
    
		var total = _chat.GetUnreadCounts().Values.Sum();
		_badgeLabel.Text = total > 99 ? "99+" : total.ToString();
		_badgeWrapper.Visible = total > 0;

	}
	
	public override void _ExitTree()
	{
		if (_chat == null) return;
		_chat.UnreadCountUpdated -= OnUnreadCountUpdated;
		_chat.UnreadCountUpdated -= UpdateBadge;
		_chat.UnreadMessagesLoaded -= OnUnreadMessagesLoaded;
	}
}
