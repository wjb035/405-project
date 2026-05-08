using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using PGEmu.Services;

public partial class ChatManager : Node
{   
    // SIGNALS
    [Signal] public delegate void DmReceivedEventHandler(string fromUser, string message, string sentAt);
    [Signal] public delegate void UserCameOnlineEventHandler(string username);
    [Signal] public delegate void UserWentOfflineEventHandler(string username);
    [Signal] public delegate void GroupCreatedEventHandler(string groupId, string groupName);
    [Signal] public delegate void GroupJoinedEventHandler(string groupId, string groupName, string[] members);
    [Signal] public delegate void GroupLeftEventHandler(string groupId);
    [Signal] public delegate void GroupMessageReceivedEventHandler(string groupId, string user, string message, string sentAt);
    [Signal] public delegate void GroupMemberJoinedEventHandler(string groupId, string user);
    [Signal] public delegate void GroupMemberLeftEventHandler(string groupId, string user);
    [Signal] public delegate void GroupInviteReceivedEventHandler(string groupId, string groupName, string invitedBy);
    [Signal] public delegate void HistoryLoadedEventHandler(string contextId, string messagesJson);
    [Signal] public delegate void ErrorReceivedEventHandler(string message);
    [Signal] public delegate void UnreadCountUpdatedEventHandler();
    [Signal] public delegate void UnreadMessagesLoadedEventHandler(string messagesJson);
    
    // State handling
    private WebSocketPeer _ws = new WebSocketPeer();
    private string _serverUrl = "http://localhost:5276";
    private string _hubPath = "/chathub";
    private bool _connected = false;
    
    private Dictionary<string, int> _unreadCounts = new();

    public Dictionary<string, int> GetUnreadCounts() => _unreadCounts;
    private List<Dictionary<string, object>> _unreadMessages = new();
    public List<Dictionary<string, object>> GetUnreadMessages() => _unreadMessages;

    public string Username { get; set; } = "";
    public string CurrentDmRecipient { get; set; } = "";
    public bool IsConnected => _connected;
    
    private static readonly char RecordSeparator = (char)0x1E;
    
    
    // Connection
    // Create an HTTP call to start the connection token negotiation
    public void ConnectToChat()
    {
        // If username still empty, try to grab it now
        if (string.IsNullOrEmpty(Username))
            Username = AuthService.Instance.Username;

        if (string.IsNullOrEmpty(Username))
        {
            GD.PrintErr("ChatManager: Cant connect, username is empty");
            return;
        }
        
        var url = _serverUrl + _hubPath + "/negotiate?negotiateVersion=1";
        GD.Print("Negotiating at: " + url);
        
        var http = new HttpRequest();
        AddChild(http);
        http.RequestCompleted += (result, responseCode,headers, body)
            => OnNegotiateComplete(result, responseCode, headers, body, http);
        GD.Print($"ChatManager instance: {GetInstanceId()}");
        http.Request(
            url,
            new string[] { "Content-Type: application/json" },
            HttpClient.Method.Post
        );
    }
    public void Disconnect()
    {
        GD.Print("ChatManager Disconnect called");

        _connected = false;

        try
        {
            _ws.Close();
        }
        catch { }

        _ws = new WebSocketPeer();

        _unreadCounts.Clear();
        _unreadMessages.Clear();
        CurrentDmRecipient = "";
    }
    
    // Comoplete the negotiation
    private void OnNegotiateComplete(long result, long responseCode, string[] headers, byte[] body, HttpRequest http)
    {
        http.QueueFree();
        GD.Print("Negotiate response code: " + responseCode);
        
        if (responseCode != 200)
        {
            GD.PrintErr("Negotiate failed: " + responseCode);
            return;
        }

        var json = JsonDocument.Parse(body);
        string token = "";
        if (json.RootElement.TryGetProperty("connectionToken", out var tokenProp))
            token = tokenProp.GetString() ?? "";

        string wsUrl = _serverUrl.Replace("http://", "ws://") + _hubPath;
        if (!string.IsNullOrEmpty(token))
            wsUrl += "?id=" + Uri.EscapeDataString(token);

        GD.Print("Connecting WebSocket to: " + wsUrl);
        _ws.ConnectToUrl(wsUrl);
    }
    
    // Polls the websocket every frame for updates
    public override void _Process(double delta)
    {
        _ws.Poll();
        var state = _ws.GetReadyState();

        if (state == WebSocketPeer.State.Open)
        {
            if (!_connected)
            {
                _connected = true;
                SendHandshake();
            }
            while (_ws.GetAvailablePacketCount() > 0)
                HandlePacket(_ws.GetPacket());
        }
        else if (state == WebSocketPeer.State.Closed && _connected)
        {
            _connected = false;
        }
    }

    // Two step handshake for godot to communicate with the server
    private async void SendHandshake()
    {
        GD.Print("Sending handshake...");
        
        _ws.SendText("{\"protocol\":\"json\",\"version\":1}" + RecordSeparator);
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        
        GD.Print("Registering username: " + Username);
        Invoke("Register", new object[] { Username });
    }
    
    // Packet handling, prcoeses every message that arrives from the server
    private void HandlePacket(byte[] packet)
    {
        var text = System.Text.Encoding.UTF8.GetString(packet);
        GD.Print("Packet received: " + text);
        var messages = text.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries);

        // Loop through all messages skipping the mepty ones
        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg)) continue;
            try
            {
                // Parse message as Json
                var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;
                
                // Read type property as an int
                int type;
                if (root.TryGetProperty("type", out var typeProp))
                {
                    type = typeProp.GetInt32();
                }
                else
                {
                    type = 0;
                }

                switch (type)
                {
                    // If typpe is 1, read the target string and its arguments. Pass to handleinvocation. If type is 6, its a ping.
                    case 1:
                        string target;
                        if (root.TryGetProperty("target", out var t))
                        {
                            target = t.GetString() ?? "";
                        }
                        else
                        {
                            target = "";
                        }
                        
                        JsonElement args;
                        if (root.TryGetProperty("arguments", out var a))
                        {
                            args = a;
                        }
                        else
                        {
                            args = default;
                        }
                        
                        HandleInvocation(target, args);
                        break;
                    case 6:
                        _ws.SendText("{\"type\":6}" + RecordSeparator);
                        break;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr("Packet parse error: " + e.Message);
            }
        }
    }
    
    // Matches incoming server calls to the right signal
    private void HandleInvocation(string target, JsonElement args)
    {
        switch (target)
        {
            case "ReceiveDirectMessage":
                EmitSignal(SignalName.DmReceived,
                    args[0].GetString(), args[1].GetString(),
                    args.GetArrayLength() > 2 ? args[2].GetString() : "");
                break;
            case "UserOnline":
                EmitSignal(SignalName.UserCameOnline, args[0].GetString());
                break;
            case "UserOffline":
                EmitSignal(SignalName.UserWentOffline, args[0].GetString());
                break;
            case "GroupCreated":
                EmitSignal(SignalName.GroupCreated, args[0].GetString(), args[1].GetString());
                break;
            case "GroupJoined":
                var members = JsonSerializer.Deserialize<string[]>(args[2].GetRawText()) ?? Array.Empty<string>();
                EmitSignal(SignalName.GroupJoined, args[0].GetString(), args[1].GetString(), members);
                break;
            case "GroupLeft":
                EmitSignal(SignalName.GroupLeft, args[0].GetString());
                break;
            case "ReceiveGroupMessage":
                EmitSignal(SignalName.GroupMessageReceived,
                    args[0].GetString(), args[1].GetString(), args[2].GetString(),
                    args.GetArrayLength() > 3 ? args[3].GetString() : "");
                break;
            case "GroupMemberJoined":
                EmitSignal(SignalName.GroupMemberJoined, args[0].GetString(), args[1].GetString());
                break;
            case "GroupMemberLeft":
                EmitSignal(SignalName.GroupMemberLeft, args[0].GetString(), args[1].GetString());
                break;
            case "GroupInviteReceived":
                EmitSignal(SignalName.GroupInviteReceived,
                    args[0].GetString(), args[1].GetString(), args[2].GetString());
                break;
            case "Error":
                EmitSignal(SignalName.ErrorReceived, args[0].GetString());
                break;
        }
    }
    
    // Invoke helper to send HTTP requests to the server
    private void Invoke(string target, object[] arguments)
    {
        if (!_connected) return;
        var payload = JsonSerializer.Serialize(new
        {
            type = 1,
            target,
            arguments
        });
        _ws.SendText(payload + RecordSeparator);
    }
    
    // HTTP Helper
    private void HttpGet(string url, Action<string> callback)
    {
        var http = new HttpRequest();
        AddChild(http);
        http.RequestCompleted += (result, responseCode, headers, body) =>
        {
            http.QueueFree();
            if (responseCode != 200)
            {
                GD.PrintErr("HTTP error: " + responseCode);
                return;
            }
            callback(System.Text.Encoding.UTF8.GetString(body));
        };
        http.Request(url);
    }
    
    // DM Api, handles unread messages and unread counts too
    public void SetDmRecipient(string user) => CurrentDmRecipient = user;

    public void SendDm(string message) =>
        Invoke("SendDM", new object[] { Username, CurrentDmRecipient, message });

    public void LoadDmHistory(string otherUser, string before = "")
    {
        string url = $"{_serverUrl}/api/chat/dm?user1={Username}&user2={otherUser}";
        if (!string.IsNullOrEmpty(before))
            url += "&before=" + Uri.EscapeDataString(before);
        HttpGet(url, json => EmitSignal(SignalName.HistoryLoaded, otherUser, json));
    }
    
    public void LoadUnreadMessages()
    {
        string url = $"{_serverUrl}/api/chat/unread?user={Username}";
        HttpGet(url, json =>
        {
            EmitSignal(SignalName.UnreadMessagesLoaded, json);
        });
    }
    public void LoadUnreadCounts()
    {
        string url = $"{_serverUrl}/api/chat/unread-count?user={Username}";
        HttpGet(url, json =>
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);

            if (data != null)
                _unreadCounts = data;

            EmitSignal(SignalName.UnreadCountUpdated);
        });
    }
    public void SetUnreadCounts(Dictionary<string, int> counts)
    {
        _unreadCounts = counts;
        EmitSignal(SignalName.UnreadCountUpdated);
    }
    public void MarkDmAsRead(string otherUser)
    {
        string url = $"{_serverUrl}/api/chat/mark-read";

        var body = JsonSerializer.Serialize(new
        {
            username = Username,
            otherUser = otherUser
        });

        var http = new HttpRequest();
        AddChild(http);

        http.RequestCompleted += (result, code, headers, bodyBytes) =>
        {
            http.QueueFree();
            if (code == 200)
            {
                _unreadCounts.Remove(otherUser);
                EmitSignal(SignalName.UnreadCountUpdated);
            }
        };

        http.Request(
            url,
            new[] { "Content-Type: application/json" },
            HttpClient.Method.Post,
            body
        );
    }
    
    // Group chat API
    public void CreateGroup(string groupName) =>
        Invoke("CreateGroup", new object[] { Username, groupName });

    public void InviteToGroup(string groupId, string targetUser) =>
        Invoke("InviteToGroup", new object[] { Username, groupId, targetUser });

    public void AcceptInvite(string groupId) =>
        Invoke("AcceptGroupInvite", new object[] { Username, groupId });

    public void DeclineInvite(string groupId) =>
        Invoke("DeclineGroupInvite", new object[] { Username, groupId });

    public void LeaveGroup(string groupId) =>
        Invoke("LeaveGroup", new object[] { Username, groupId });

    public void RemoveFromGroup(string groupId, string targetUser) =>
        Invoke("RemoveFromGroup", new object[] { Username, groupId, targetUser });

    public void SendGroupMessage(string groupId, string message) =>
        Invoke("SendGroupMessage", new object[] { Username, groupId, message });

    public void LoadGroupHistory(string groupId, string before = "")
    {
        string url = $"{_serverUrl}/api/chat/group?groupId={groupId}";
        if (!string.IsNullOrEmpty(before))
            url += "&before=" + Uri.EscapeDataString(before);
        HttpGet(url, json => EmitSignal(SignalName.HistoryLoaded, groupId, json));
    }
}
