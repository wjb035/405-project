using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using PGEmu.Services;

public partial class ChatOverlay : CanvasLayer
{
	// Main stuff
	private Panel _panel;
	private TabBar _tabBar;
	private Button _closeButton;

	// Friends tab
	private VBoxContainer _friendsTab;
	private LineEdit _searchBar;
	private VBoxContainer _friendsList;

	// Chat tab
	private VBoxContainer _chatTab;
	private Label _chatTitle;
	private Button _backButton;
	private RichTextLabel _messageLog;
	private Button _loadMoreButton;
	private LineEdit _inputField;
	private Button _sendButton;

	// Dms
	private bool _isOpen = false;
	private string _currentDmUser = "";
	private string _oldestMessageTime = "";
	private Dictionary<string, int> _unread = new();
	private HashSet<string> _onlineFriends = new();
	private List<(string fromUser, string message, string sentAt)> _missedMessages = new();
    
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
		_messageLog = GetNode<RichTextLabel>("Panel/VBox/MainArea/ChatTab/MessageHistory");
		_loadMoreButton = GetNode<Button>("Panel/VBox/MainArea/ChatTab/LoadMoreButton");
		_inputField = GetNode<LineEdit>("Panel/VBox/MainArea/ChatTab/InputRow/TextField");
		_sendButton = GetNode<Button>("Panel/VBox/MainArea/ChatTab/InputRow/SendButton");
		
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
		_loadMoreButton.Pressed += OnLoadMore;

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
		GoToFriendsTab();
		_ = RefreshFriendsList();
	}

	public void CloseOverlay()
	{
		_isOpen = false;
		_panel.Hide();
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
		
		// Loops through the friends and creates a container for each friend.
		foreach (var friend in friends)
		{
			if (!string.IsNullOrEmpty(filter) &&
			    !friend.Contains(filter, StringComparison.OrdinalIgnoreCase))
				continue;
			
			
			var row = new HBoxContainer();
			row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			
			// Each row has a status indicator
			var dot = new ColorRect();
			dot.CustomMinimumSize = new Vector2(10, 10);
			dot.Color = _onlineFriends.Contains(friend)
				? new Color(0.42f, 1f, 0.42f, 1f)
				: new Color(0.33f, 0.33f, 0.44f, 1f);
			row.AddChild(dot);
			
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
				var badgeStyle = new StyleBoxFlat
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
				};
				badge.AddThemeStyleboxOverride("normal", badgeStyle);
				row.AddChild(badge);
			}
			_friendsList.AddChild(row);
			
			UiStyle.StyleTopBarButton(btn);
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
		_chat.SetDmRecipient(username);
		_messageLog.Clear();
		_loadMoreButton.Hide();
		_unread.Remove(username);
		_ = RefreshFriendsList();
		GoToChatTab(username);
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
		
		
		GD.Print($"=== OnDmReceived ===");
		GD.Print($"fromUser: '{fromUser}'");
		GD.Print($"_chat.Username: '{_chat.Username}'");
		GD.Print($"_currentDmUser: '{_currentDmUser}'");
		GD.Print($"isMine: {isMine}");
		GD.Print($"conversationWith: '{conversationWith}'");
		GD.Print($"_isOpen: {_isOpen}");
		GD.Print($"_tabBar.CurrentTab: {_tabBar.CurrentTab}");
		GD.Print($"condition result: {_isOpen && _tabBar.CurrentTab == 1 && _currentDmUser == conversationWith}");

		if (_isOpen && _tabBar.CurrentTab == 1 && _currentDmUser == fromUser)
		{
			GD.Print("APPENDING MESSAGE");
			GD.Print($"_messageLog is null: {_messageLog == null}");
			AppendMessage(fromUser, message, sentAt);
		}

		else
		{
			if (!isMine)
			{
				GD.Print("GOING TO UNREAD");
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
		if (!string.IsNullOrEmpty(_oldestMessageTime) && messages.Count > 0)
		{
			string existing = _messageLog.Text;
			_messageLog.Clear();
			foreach (var msg in messages)
				AppendMessage(GetString(msg, "fromUser"), GetString(msg, "content"), GetString(msg, "sentAt"));
			_messageLog.AppendText(existing);
		}
		else
		{
			_messageLog.Clear();
			foreach (var msg in messages)
				AppendMessage(GetString(msg, "fromUser"), GetString(msg, "content"), GetString(msg, "sentAt"));
		}

		if (messages.Count > 0)
		{
			_oldestMessageTime = GetString(messages[0], "sentAt");
			_loadMoreButton.Show();
		}
		else
		{
			_loadMoreButton.Hide();
		}
	}
	
	// Helper slop
	private void AppendMessage(string fromUser, string message, string sentAt = "")
	{
		
		// Changes formatting based on if its you or the recipient messaging
		bool isMe = fromUser == _chat.Username;
		string color = isMe ? "b980ff" : "ffffff";
		string time = FormatTime(sentAt);
		string align = isMe ? "[right]" : "";
		string alignEnd = isMe ? "[/right]" : "";
		var text =
			$"{align}[color=555577][{time}][/color] [color={color}][b]{fromUser}:[/b][/color] {message}{alignEnd}\n";

		GD.Print("Appending text: " + text);
		_messageLog.AppendText(text);
		GD.Print("Message log text length after append: " + _messageLog.Text.Length);
		
		_messageLog.ScrollToLine(_messageLog.GetLineCount());
	}

	// Converts time to the right time zone and formats it right
	private string FormatTime(string isoString)
	{
		if (string.IsNullOrEmpty(isoString)) return "";
		if (DateTime.TryParse(isoString, out var dt))
			return dt.ToLocalTime().ToString("HH:mm");
		return "";
	}
	
	private void ApplyAesthetic()
	{
		// Buttons
		UiStyle.StyleTopBarButton(_closeButton);
		UiStyle.AddHoverFeedback(_closeButton);
		UiStyle.ApplyParallaxShadow(_closeButton);
		
		UiStyle.StyleTopBarButton(_backButton);
		UiStyle.AddHoverFeedback(_backButton);
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
