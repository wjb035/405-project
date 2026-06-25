using Godot;
using System;
using PGEmu.Services;
using PGEmu.UI;

namespace PGEmu.UI;

public partial class MissedMessageItem : InboxItem
{
	private Label _fromLabel;
	private Label _previewLabel;
	private Label _timeLabel;
	private Button _replyButton;
	private string _fromUser;
	public static event Action RequestCloseInbox;

	public override void _Ready()
	{
		GD.Print("MissedMessageItem _Ready fired");
		
		_fromLabel = GetNode<Label>("HBox/FromLabel");
		_previewLabel = GetNode<Label>("HBox2/PreviewLabel");
		_timeLabel = GetNode<Label>("HBox2/TimeLabel");
		_replyButton = GetNode<Button>("HBox/ReplyButton");

		GD.Print($"fromLabel null? {_fromLabel == null}");
		
		UiStyle.AddHoverFeedback(_replyButton);
		_replyButton.Pressed += OnReply;
	}
	
	public void Setup(string fromUser, string preview, string sentAt, int count)
	{
		_fromUser = fromUser;
		
		_fromLabel ??= GetNode<Label>("HBox/FromLabel");
		_previewLabel ??= GetNode<Label>("HBox2/PreviewLabel");
		_timeLabel ??= GetNode<Label>("HBox2/TimeLabel");
		_replyButton ??= GetNode<Button>("HBox/ReplyButton");
		
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
		overlay.OpenOverlay();
		overlay.OpenDm(_fromUser);
		overlay.GetNode<ChatManager>("/root/ChatManager")
			.MarkDmAsRead(_fromUser);
		// Close the inbox
		FriendInbox.Instance?.HidePopup();
		
	}

	private string FormatTime(string isoString)
	{
		if (string.IsNullOrEmpty(isoString)) return "";
		if (System.DateTime.TryParse(isoString, out var dt))
			return dt.ToLocalTime().ToString("HH:mm");
		return "";
	}
}
