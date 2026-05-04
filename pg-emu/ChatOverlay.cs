using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PGEmu.Services;

public partial class ChatOverlay : CanvasLayer
{
	// Main stuff
	private Panel _panel;
	private TabBar _tabBar;
	private Button _closeButton;
	private float _restingX;
	
	// Friends tab
	private VBoxContainer _friendsTab;
	private LineEdit _searchBar;
	private VBoxContainer _friendsList;
	private Dictionary<string, DateTime> _lastMessageTime = new();
	
	// Chat tab
	private VBoxContainer _chatTab;
	private Label _chatTitle;
	private Button _backButton;
	private Button _loadMoreButton;
	private LineEdit _inputField;
	private Button _sendButton;
	private bool _isLoadingMore = false;
	private bool _suppressScrollToBottom = false;
	
	// Message Formatting
	private ScrollContainer _messageScroll;
	private VBoxContainer _messageContainer;
	private readonly Dictionary<string, Texture2D> _avatarCache = new();
	private readonly System.Net.Http.HttpClient _httpClient = new();
	private string _myAvatarUrl = "";
	private string _theirAvatarUrl = "";
	private string _lastMessageSender = "";
	

	// Dms
	private bool _isOpen = false;
	private string _currentDmUser = "";
	private string _oldestMessageTime = "";
	private Dictionary<string, int> _unread = new();
	private HashSet<string> _onlineFriends = new();
	
	private List<(string fromUser, string message, string sentAt)> _missedMessages = new();
	private List<(string fromUser, string message, string sentAt)> _displayedMessages = new();
	
	private ChatManager _chat;
	
	public override void _Ready()
	{
		_chat = GetNode<ChatManager>("/root/ChatManager");
		
		_panel = GetNode<Panel>("Panel");
		_tabBar = GetNode<TabBar>("Panel/VBox/Header/TabBar");
		_closeButton = GetNode<Button>("Panel/VBox/Header/CloseButton");

		_friendsTab = GetNode<VBoxContainer>("Panel/VBox/MainArea/FriendsTab");
		_searchBar = GetNode<LineEdit>("Panel/VBox/MainArea/FriendsTab/SearchBar");
		_friendsList = GetNode<VBoxContainer>("Panel/VBox/MainArea/FriendsTab/FriendsList");

		_chatTab = GetNode<VBoxContainer>("Panel/VBox/MainArea/ChatTab");
		_chatTitle = GetNode<Label>("Panel/VBox/MainArea/ChatTab/ChatHeader/ChatTitle");
		_backButton = GetNode<Button>("Panel/VBox/MainArea/ChatTab/ChatHeader/BackButton");
		_messageScroll = GetNode<ScrollContainer>("Panel/VBox/MainArea/ChatTab/MessageScroll");
		_messageContainer = GetNode<VBoxContainer>("Panel/VBox/MainArea/ChatTab/MessageScroll/MessageContainer");
		_loadMoreButton = GetNode<Button>("Panel/VBox/MainArea/ChatTab/LoadMoreButton");
		_inputField = GetNode<LineEdit>("Panel/VBox/MainArea/ChatTab/InputRow/TextField");
		_sendButton = GetNode<Button>("Panel/VBox/MainArea/ChatTab/InputRow/SendButton");
		_restingX = _panel.Position.X;
		
		// Chat is hidden on start, it should be the friends tab first
		_panel.Hide();
		_loadMoreButton.Hide();
		_chatTab.Hide();
		
		// UI events
		_closeButton.Pressed += CloseOverlay;
		_backButton.Pressed += GoToFriendsTab;
		_tabBar.TabChanged += OnTabChanged;
		_searchBar.TextChanged += filter => RefreshFriendsList(filter);
		_sendButton.Pressed += OnSend;
		_inputField.TextSubmitted += _ => OnSend();
		// _loadMoreButton.Pressed += OnLoadMore;
		_messageScroll.GetVScrollBar().ValueChanged += OnScrollValueChanged;

		// ChatManager signals
		_chat.DmReceived += OnDmReceived;
		_chat.UserCameOnline += OnUserCameOnline;
		_chat.UserWentOffline += OnUserWentOffline;
		_chat.HistoryLoaded += OnHistoryLoaded;
		
		ApplyAesthetic();
	}


	
	// Open chat by pressing C
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			var focused = GetViewport().GuiGetFocusOwner();
			if (focused is LineEdit || focused is TextEdit)
				return;
			
			if (key.Keycode == Key.C)
				ToggleOverlay();
		}
	}
	
	// Overlay toggling ona nd off
	public void ToggleOverlay()
	{
		if (_isOpen) CloseOverlay();
		else OpenOverlay();
	}

	public void OpenOverlay()
	{
		_isOpen = true;
		_panel.Show();
		
		// Animate
		_panel.Position = new Vector2(_restingX + _panel.Size.X, _panel.Position.Y); 
		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(_panel, "position:x", _restingX, 0.28f);
		
		GoToFriendsTab();
		_ = RefreshFriendsList();
	}

	public void CloseOverlay()
	{
		_isOpen = false;
		
		var tween = CreateTween();
		var exitX = _restingX + _panel.Size.X;
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.In);
		tween.TweenProperty(_panel, "position:x", exitX, 0.22f);

		tween.TweenCallback(Callable.From(() =>
		{
			_panel.Hide();
			_panel.Position = new Vector2(_restingX, _panel.Position.Y);
		}));
	}
	
	
	// TABS SWAPAPIN
	private void OnTabChanged(long tab)
	{
		_friendsTab.Visible = tab == 0;
		_chatTab.Visible = tab == 1;
	}

	private void GoToFriendsTab()
	{
		_tabBar.CurrentTab = 0;
		_friendsTab.Show();
		_chatTab.Hide();
		_currentDmUser = "";
	}

	private void GoToChatTab(string username)
	{
		_tabBar.CurrentTab = 1;
		_friendsTab.Hide();
		_chatTab.Show();
		_chatTitle.Text = username;
	}

	
	// Friends tab handling
	// Refreshes the friends list, this is the meat and potatoes of everything
	private async System.Threading.Tasks.Task RefreshFriendsList(string filter = "")
	{
		// Clear whatevers in there
		foreach (Node child in _friendsList.GetChildren())
			child.QueueFree();
		
		var friends = await FriendService.Instance.GetFriendUsernames();
		
		var sorted = friends.OrderByDescending(f =>
			_lastMessageTime.TryGetValue(f, out var t) ? t : DateTime.MinValue).ToList();
		
		// Loops through the friends and creates a container for each friend.
		foreach (var friend in sorted)
		{
			if (!string.IsNullOrEmpty(filter) &&
			    !friend.Contains(filter, StringComparison.OrdinalIgnoreCase))
				continue;
			
			
			var row = new HBoxContainer();
			row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			
			
			// Avatar in a cirlce 
			var avatarWrapper = new Control();
			avatarWrapper.CustomMinimumSize = new Vector2(40, 40);
			avatarWrapper.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

			
			// ACtual avatar
			var avatarImg = new TextureRect();
			avatarImg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			avatarImg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
			avatarImg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			avatarImg.OffsetLeft = 2;
			avatarImg.OffsetTop = 2;
			avatarImg.OffsetRight = -2;
			avatarImg.OffsetBottom = -2;

			var avatarMat = new ShaderMaterial();
			avatarMat.Shader = GD.Load<Shader>("res://ShaderSlop/circle.gdshader");
			avatarImg.Material =  avatarMat;
			
			avatarWrapper.AddChild(avatarImg);
			
			// Online status indicator
			var ring = new ColorRect();
			ring.CustomMinimumSize = new Vector2(10, 10);
			
			ring.AnchorLeft = 1;
			ring.AnchorTop = 1;
			ring.AnchorRight = 1;
			ring.AnchorBottom = 1;

			ring.OffsetLeft = -12;
			ring.OffsetTop = -12;
			ring.OffsetRight = -2;
			ring.OffsetBottom = -2;
			
			ring.Color = _onlineFriends.Contains(friend)
				? new Color(0.42f, 1f, 0.42f)
				: new Color(0.44f, 0.40f, 0.62f);
			var ringShaderMat = new ShaderMaterial();
			ringShaderMat.Shader = GD.Load<Shader>("res://ShaderSlop/circle.gdshader");
			ring.Material = ringShaderMat;
			ring.MouseFilter = Control.MouseFilterEnum.Ignore;
			avatarWrapper.AddChild(ring);
			row.AddChild(avatarWrapper);
			
			// Load avatar texture
			string capturedFriend = friend;
			_ = LoadFriendAvatarAsync(capturedFriend, avatarImg);

			
			/* Each row has a status indicator
			var dot = new ColorRect();
			dot.CustomMinimumSize = new Vector2(10, 10);
			dot.Color = _onlineFriends.Contains(friend)
				? new Color(0.42f, 1f, 0.42f, 1f)
				: new Color(0.33f, 0.33f, 0.44f, 1f);
			row.AddChild(dot);
			*/
			
			// Clickable friends, that elt you open dms with them
			var btn = new Button();
			btn.Text = friend;
			btn.Flat = false;
			btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			btn.Alignment = HorizontalAlignment.Left;
			string captured = friend;
			btn.Pressed += () => OpenDm(captured);
			row.AddChild(btn);
			
			
			if (_unread.TryGetValue(friend, out int count) && count > 0)
			{
				var badge = new Label();
				badge.Text = count.ToString();
				badge.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
				badge.AddThemeFontSizeOverride("font_size", 11);
				badge.AddThemeStyleboxOverride("normal", new StyleBoxFlat
				{
					BgColor = new Color(0.55f, 0.2f, 0.8f, 1f),
					CornerRadiusTopLeft = 999,
					CornerRadiusTopRight = 999,
					CornerRadiusBottomLeft = 999,
					CornerRadiusBottomRight = 999,
					ContentMarginLeft = 6f,
					ContentMarginRight = 6f,
					ContentMarginTop = 2f,
					ContentMarginBottom = 2f,
				});
				row.AddChild(badge);
			}
			_friendsList.AddChild(row);
			
			UiStyle.StyleTopBarButton(btn);
			UiStyle.ApplyParallaxShadow(btn);
			UiStyle.AddHoverFeedback(btn);
		}
		
	}
	
	
	// Onlinie handling
	private void OnUserCameOnline(string username)
	{
		_onlineFriends.Add(username);
		_ = RefreshFriendsList();
	}

	private void OnUserWentOffline(string username)
	{
		_onlineFriends.Remove(username);
		_ = RefreshFriendsList();
	}

	// Chat tab handling
	public void OpenDm(string username)
	{
		_currentDmUser = username;
		_oldestMessageTime = "";
		_displayedMessages.Clear();
		_lastMessageSender = "";
		_myAvatarUrl = "";
		_theirAvatarUrl = "";
		_chat.SetDmRecipient(username);
		ClearMessages();
		_loadMoreButton.Hide();
		_unread.Remove(username);
		_ = RefreshFriendsList();
		GoToChatTab(username);
		_ = OpenDmAsync(username);
	}
	
	private async System.Threading.Tasks.Task OpenDmAsync(string username)
	{
		await PreloadAvatars(username);
		_chat.LoadDmHistory(username);
	}
	
	private void OnSend()
	{
		GD.Print($"Sending as: '{_chat.Username}' to: '{_currentDmUser}'");
		string text = _inputField.Text.Trim();
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_currentDmUser))
			return;
		_chat.SendDm(text);
		_inputField.Clear();
	}

	private void OnLoadMore()
	{
		if (string.IsNullOrEmpty(_oldestMessageTime)) return;
		_chat.LoadDmHistory(_currentDmUser, _oldestMessageTime);
	}

	
	// Signalslop
	private void OnDmReceived(string fromUser, string message, string sentAt)
	{
		bool isMine = fromUser == _chat.Username;
		string conversationWith = isMine ? _currentDmUser : fromUser;
		
		if (DateTime.TryParse(sentAt, out var msgTime))
			_lastMessageTime[conversationWith] = msgTime;
		
		if (_isOpen && _tabBar.CurrentTab == 1 && _currentDmUser == conversationWith)
		{
			_displayedMessages.Add((fromUser, message, sentAt));
			RebuildMessageLog();
		}

		else
		{
			if (!isMine)
			{ 
				_unread[fromUser] = _unread.GetValueOrDefault(fromUser, 0) + 1;
				_ = RefreshFriendsList();
				if (!_isOpen)
					_missedMessages.Add((fromUser, message, sentAt));
			}
			
		}
		
	}
	
	// For  messages you missed
	public List<(string fromUser, string message, string sentAt)> GetAndClearMissedMessages()
	{
		var copy = new List<(string, string, string)>(_missedMessages);
		_missedMessages.Clear();
		return copy;
	}
	
	private void OnHistoryLoaded(string contextId, string messagesJson)
	{
		if (contextId != _currentDmUser) return;

		var messages = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(messagesJson)
		               ?? new List<Dictionary<string, JsonElement>>();

		string GetString(Dictionary<string, JsonElement> msg, string key)
		{
			if (msg.TryGetValue(key, out var val))
				return val.ValueKind == JsonValueKind.String ? val.GetString() ?? "" : val.ToString();
			return "";
		}
		
		// Put older messages above the existing ones
		var incoming = messages
			.Select(m => (GetString(m, "fromUser"), GetString(m, "content"), GetString(m, "sentAt")))
			.ToList();

		if (!string.IsNullOrEmpty(_oldestMessageTime))
			_displayedMessages = incoming.Concat(_displayedMessages).ToList();
		else
			_displayedMessages = incoming;
		
		// If youre loading more messages, don't autoscroll to the bottom
		_suppressScrollToBottom = !string.IsNullOrEmpty(_oldestMessageTime) && _displayedMessages.Count > 0;

		if (messages.Count > 0)
			_oldestMessageTime = GetString(messages[0], "sentAt");
		
		// Rebuild all messages with correct isLastInBlock values
		RebuildMessageLog();
		if (_displayedMessages.Count > 0 && DateTime.TryParse(
			    _displayedMessages[^1].sentAt, out var lastTime))
		{
			_lastMessageTime[_currentDmUser] = lastTime;
		}
		_suppressScrollToBottom = false;
		_loadMoreButton.Hide();
	}
	
	// For loading mroe messages
	private async void OnScrollValueChanged(double value)
	{
		if (_isLoadingMore || string.IsNullOrEmpty(_oldestMessageTime)) return;
		
		// If scroll bar isn't at the top, dont trigger the load more
		var scrollBar = _messageScroll.GetVScrollBar();
		if (value > scrollBar.MaxValue * 0.12) return; 
		
		_isLoadingMore = true;
		ShowLoadingIndicator(true);
		
		var oldMax = scrollBar.MaxValue;

		_chat.LoadDmHistory(_currentDmUser, _oldestMessageTime);
		
		// 2 frame delay for rebuildmessagelog
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		
		if (GodotObject.IsInstanceValid(_messageScroll))
		{
			var addedHeight = _messageScroll.GetVScrollBar().MaxValue - oldMax;
			_messageScroll.ScrollVertical = (int)(value + addedHeight);
		}

		ShowLoadingIndicator(false);
		_isLoadingMore = false;
		
	}
	private void ShowLoadingIndicator(bool visible)
	{
		_loadMoreButton.Text = visible ? "Loading messages..." : "";
		_loadMoreButton.Disabled = true;
		_loadMoreButton.Visible = visible;
	}
	
	// Rebuilds the current message log 
	private void RebuildMessageLog()
	{
		ClearMessages();
		for (int i = 0; i < _displayedMessages.Count; i++)
		{
			var (fromUser, message, sentAt) = _displayedMessages[i];
			// Last in block = next message is from someone else, or this is the last message
			bool isLastInBlock = i == _displayedMessages.Count - 1 ||
			                     _displayedMessages[i + 1].fromUser != fromUser;
			AppendMessage(fromUser, message, sentAt, isLastInBlock);
		}
		if (!_suppressScrollToBottom)
			ScrollToBottom();
	}
	
	// Helper slop
	private void AppendMessage(string fromUser, string message, string sentAt = "", bool isLastInBlock = false)
	{
		// Changes formatting based on if its you or the recipient messaging
		bool isMe = fromUser == _chat.Username;
		_lastMessageSender = fromUser;
		
		var row = new VBoxContainer();
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		row.AddThemeConstantOverride("separation", 2);
		
		var messageRow = new HBoxContainer();
		messageRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		
		// CHAT BUBBLE
		var bubble = new PanelContainer();
		bubble.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		
		var bubbleStyle = new StyleBoxFlat
		{
			BgColor = isMe
				? new Color(0.37f, 0.18f, 0.58f, 0.95f)   // purple for the user
				: new Color(0.18f, 0.16f, 0.28f, 0.95f),   // dark for others
			BorderColor = isMe
				? new Color(0.65f, 0.42f, 0.92f, 0.7f)
				: new Color(0.44f, 0.40f, 0.62f, 0.5f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusBottomLeft = isMe ? 16 : 4,
			CornerRadiusBottomRight = isMe ? 4 : 16,
			CornerRadiusTopLeft = 16,
			CornerRadiusTopRight = 16,
			ContentMarginLeft = 12f,
			ContentMarginRight = 12f,
			ContentMarginTop = 8f,
			ContentMarginBottom = 8f,
		};
		bubble.AddThemeStyleboxOverride("panel", bubbleStyle);
		
		// Message in the bubble yo, resizes based on how many letters are sent
		var msgLabel = new Label();
		msgLabel.Text = message;
		msgLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		msgLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 1f, 0.98f));
		msgLabel.AddThemeFontSizeOverride("font_size", 14);
		
		float maxBubbleWidth = 200f;
		float naturalWidth = 0f;
		
		// Resizing based on how much text is ssent
		var font = msgLabel.GetThemeFont("font");
		int fontSize = msgLabel.GetThemeFontSize("font_size");
		if (font != null)
			naturalWidth = font.GetStringSize(message, HorizontalAlignment.Left, -1, fontSize).X;
		else
			naturalWidth = message.Length * 8f;
		
		// Add padding to match bubble margins
		float labelWidth = Mathf.Min(naturalWidth, maxBubbleWidth) + 1f;
		msgLabel.CustomMinimumSize = new Vector2(labelWidth, 0);
				
		bubble.AddChild(msgLabel);
		
		// ASSEMBLE ROW
		var spacer = new Control();
		spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		if (isMe)
		{
			// Spacer pushes bubble to the right. 
			messageRow.AddChild(spacer);
			messageRow.AddChild(bubble);
		}
		else
		{
			// Spacer keeps bubble on lef
			messageRow.AddChild(bubble);
			messageRow.AddChild(spacer);
		}
		row.AddChild(messageRow);
		

		// If sender changed, update previous last mesages avatar visibility. I want the avatar to only show up on the most recent message of each person.
		if (isLastInBlock)
		{
			var metaRow = new HBoxContainer();
			metaRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			metaRow.AddThemeConstantOverride("separation", 6);
			
			// Spots for the avatar to go
			var avatarSpace = CreateAvatarSpace(isMe);
				
			// Shows username udner message
			var usernameLabel = new Label();
			usernameLabel.Text = fromUser;
			usernameLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.63f, 0.8f, 1f));
			usernameLabel.AddThemeFontSizeOverride("font_size", 10);
			usernameLabel.HorizontalAlignment = isMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;
			
			// Time next to username
			var timeLabel = new Label();
			timeLabel.Text = FormatTime(sentAt);
			timeLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.53f, 0.65f, 1f));
			timeLabel.AddThemeFontSizeOverride("font_size", 10);
			timeLabel.HorizontalAlignment = isMe ? HorizontalAlignment.Right : HorizontalAlignment.Left;
			
			var metaSpacer = new Control();
			metaSpacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			//usernameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			//timeLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;

			if (isMe)
			{
				metaRow.AddChild(metaSpacer);
				metaRow.AddChild(timeLabel);
				metaRow.AddChild(usernameLabel);
				metaRow.AddChild(avatarSpace);
			}
			else
			{
				metaRow.AddChild(avatarSpace);
				metaRow.AddChild(usernameLabel);
				metaRow.AddChild(timeLabel);
				metaRow.AddChild(metaSpacer);
			}

			row.AddChild(metaRow);
		}
		_messageContainer.AddChild(row);

	}

	private Control CreateAvatarSpace(bool isMe)
	{
		var container = new PanelContainer();
		container.CustomMinimumSize = new Vector2(24, 24);
		container.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
		container.ClipContents = true;
		
		// Avtar circle build
		container.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.20f, 0.16f, 0.30f, 0.2f),
			CornerRadiusTopLeft = 12,
			CornerRadiusTopRight = 12,
			CornerRadiusBottomLeft = 12,
			CornerRadiusBottomRight = 12,
			BorderColor = isMe
				? new Color(0.65f, 0.42f, 0.92f, 0.4f)
				: new Color(0.44f, 0.40f, 0.62f, 0.4f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
		});

		
		var avatarImg = new TextureRect();
		avatarImg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		avatarImg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
		avatarImg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		container.AddChild(avatarImg);

		// Load avatar texture
		var avatarUrl = isMe ? _myAvatarUrl : _theirAvatarUrl;
		if (!string.IsNullOrEmpty(avatarUrl))
		{
			_ = SetAvatarAsync(avatarImg, avatarUrl);
		}

		return container;
	}

	private async System.Threading.Tasks.Task SetAvatarAsync(TextureRect rect, string url)
	{
		var texture = await LoadAvatarAsync(url);
		if (texture != null && GodotObject.IsInstanceValid(rect))
			rect.Texture = texture;
	}

	private void ScrollToBottom()
	{
		CallDeferred(nameof(DeferredScrollToBottom));
	}

	private void DeferredScrollToBottom()
	{
		if (GodotObject.IsInstanceValid(_messageScroll))
			_messageScroll.ScrollVertical = (int)_messageScroll.GetVScrollBar().MaxValue;
	}
	
	// Does what it says
	private void ClearMessages()
	{
		foreach (Node child in _messageContainer.GetChildren())
			child.QueueFree();
		_lastMessageSender = "";
	}

	// Converts time to the right time zone and formats it right
	private string FormatTime(string isoString)
	{
		if (string.IsNullOrEmpty(isoString)) return "";
		if (DateTime.TryParse(isoString, out var dt))
			return dt.ToLocalTime().ToString("HH:mm");
		return "";
	}
	
	
	// AVatar fetching stolen from the profile screens
	private async System.Threading.Tasks.Task<Texture2D> LoadAvatarAsync(string url)
	{
		if (string.IsNullOrEmpty(url)) return null;
    
		var normalized = url.StartsWith("/") ? $"http://localhost:5276{url}" : url;
    
		if (_avatarCache.TryGetValue(normalized, out var cached))
			return cached;

		try
		{
			var bytes = await _httpClient.GetByteArrayAsync(normalized);
			var image = new Image();
			var err = image.LoadPngFromBuffer(bytes);
			if (err != Error.Ok) err = image.LoadJpgFromBuffer(bytes);
			if (err != Error.Ok) err = image.LoadWebpFromBuffer(bytes);
			if (err != Error.Ok) return null;

			var texture = ImageTexture.CreateFromImage(image);
			_avatarCache[normalized] = texture;
			return texture;
		}
		catch { return null; }
	}

	private async System.Threading.Tasks.Task LoadFriendAvatarAsync(string username, TextureRect rect)
	{
		try
		{
			// Check cache first using username as key
			if (_avatarCache.TryGetValue(username, out var cached))
			{
				if (GodotObject.IsInstanceValid(rect))
					rect.Texture = cached;
				return;
			}

			var profileService = new ProfileService();
			var profile = await profileService.GetUserProfile(username);
			if (profile == null || string.IsNullOrEmpty(profile.AvatarUrl))
				return;

			var texture = await LoadAvatarAsync(profile.AvatarUrl);
			if (texture == null || !GodotObject.IsInstanceValid(rect))
				return;

			// Cache by username so repeated opens don't re-fetch
			_avatarCache[username] = texture;
			rect.Texture = texture;
		}
		catch (Exception e)
		{
			GD.PrintErr($"LoadFriendAvatarAsync failed for {username}: {e.Message}");
		}
	}
	
	private async System.Threading.Tasks.Task PreloadAvatars(string otherUsername)
	{
		var profileService = new ProfileService();
    
		// Load my avatar
		var myProfile = await profileService.GetMyProfile();
		if (myProfile != null) 
		{
			_myAvatarUrl = myProfile.AvatarUrl ?? "";
			if (!string.IsNullOrEmpty(_myAvatarUrl))
				await LoadAvatarAsync(_myAvatarUrl);
		}
    
		// Load their avatar
		var theirProfile = await profileService.GetUserProfile(otherUsername);
		if (theirProfile != null)
		{
			_theirAvatarUrl = theirProfile.AvatarUrl ?? "";
			if (!string.IsNullOrEmpty(_theirAvatarUrl))
				await LoadAvatarAsync(_theirAvatarUrl);
		}
	}
	
	
	// GE TME OUT!
	public override void _ExitTree()
	{
		if (_chat == null) return;

		_chat.DmReceived -= OnDmReceived;
		_chat.UserCameOnline -= OnUserCameOnline;
		_chat.UserWentOffline -= OnUserWentOffline;
		_chat.HistoryLoaded -= OnHistoryLoaded;
	}
	
	private void ApplyAesthetic()
	{
		// Buttons
		UiStyle.StyleGhostNavButton(_closeButton, 0.8f);
		UiStyle.ApplyParallaxShadow(_closeButton);
		
		UiStyle.StyleTabBar(_tabBar);
		
		UiStyle.StyleGhostNavButton(_backButton, 0.8f);
		UiStyle.ApplyParallaxShadow(_backButton);
		
		UiStyle.StylePrimaryButton(_sendButton);
		UiStyle.AddHoverFeedback(_sendButton);
		UiStyle.ApplyParallaxShadow(_sendButton);

		UiStyle.StylePrimaryButton(_loadMoreButton);
		UiStyle.AddHoverFeedback(_loadMoreButton);
		UiStyle.ApplyParallaxShadow(_loadMoreButton);
		
		// Input fields
		UiStyle.StyleLineEdit(_searchBar);
		UiStyle.StyleLineEdit(_inputField);

		// Labels
		UiStyle.StyleTitleLabel(_chatTitle);
	}
	
}
