using Godot;
using System;
using PGEmu.Services;
using PGEmu.UI;

public partial class MissedMessageItem : InboxItem
{
	private Label _fromLabel;
	private Label _previewLabel;
	private Label _timeLabel;
	private Button _replyButton;
	private string _fromUser;


	public override void _Ready()
	{
		_fromLabel = GetNode<Label>("HBox/FromLabel");
		_previewLabel = GetNode<Label>("HBox2/PreviewLabel");
		_timeLabel = GetNode<Label>("HBox2/TimeLabel");
		_replyButton = GetNode<Button>("HBox/ReplyButton");

		UiStyle.StyleTopBarButton(_replyButton);
		UiStyle.AddHoverFeedback(_replyButton);
		_replyButton.Pressed += OnReply;
	}

	public void Setup(string fromUser, string preview, string sentAt, int count)
	{
		_fromUser = fromUser;
		_fromLabel.Text = fromUser;
		_previewLabel.Text = count > 1
			? $"{preview} (+{count - 1} more)"
			: preview;
		_timeLabel.Text = FormatTime(sentAt);
	}

	private void OnReply()
	{
		// Open the chat overlay directly to this DM
		var overlay = GetNode<ChatOverlay>("/root/ChatOverlay");
		overlay.OpenDm(_fromUser);
		overlay.OpenOverlay();
		overlay.GetNode<ChatManager>("/root/ChatManager")
			.MarkDmAsRead(_fromUser);
		// Close the inbox
		GetNode<FriendInbox>("/root/HomeScreen/FriendInboxPopup")?.HidePopup();
	}

	private string FormatTime(string isoString)
	{
		if (string.IsNullOrEmpty(isoString)) return "";
		if (System.DateTime.TryParse(isoString, out var dt))
			return dt.ToLocalTime().ToString("HH:mm");
		return "";
	}
}
