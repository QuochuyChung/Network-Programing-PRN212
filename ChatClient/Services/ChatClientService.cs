using System.Net.Sockets;
using System.Text.Json;
using ChatProtocol;
using ChatProtocol.Dtos;

namespace ChatClient.Services;

public class ChatClientService
{
    private static ChatClientService? _instance;
    public static ChatClientService Instance => _instance ??= new ChatClientService();

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public bool IsConnected => _tcpClient?.Connected == true;
    public string? CurrentUser { get; private set; }

    // Authentication and Session Events
    public event Action<string>? OnLoginSucceeded;
    public event Action<string>? OnLoginFailed;
    public event Action? OnDisconnected;
    public event Action<string>? OnForceLogout;

    // Data & Real-time Events
    public event Action<Message>? OnMessageReceived;
    public event Action<List<GroupDto>>? OnGroupListReceived;
    public event Action<GroupDto>? OnGroupCreated;
    public event Action<Guid, List<GroupMemberDto>>? OnMemberListReceived;
    public event Action<Guid, List<MessageHistoryItemDto>>? OnMessageHistoryReceived;
    public event Action<Message>? OnChatMessageReceived;
    public event Action<List<string>>? OnOnlineListReceived;
    public event Action<string>? OnUserOnline;
    public event Action<string>? OnUserOffline;
    public event Action<List<string>>? OnUserListReceived;
    public event Action<string>? OnErrorMessageReceived;

    private readonly object _sendLock = new();
    private CancellationTokenSource? _listenCts;

    public void Send(Message message)
    {
        lock (_sendLock)
        {
            if (_stream != null)
            {
                try
                {
                    FrameWriter.WriteMessage(_stream, message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SEND ERROR] {ex.Message}");
                    Disconnect();
                }
            }
        }
    }

    public async Task<(bool Success, string Message)> ConnectAndLoginAsync(
        string host,
        int port,
        string username,
        string password = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_tcpClient == null || !_tcpClient.Connected)
            {
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port, cancellationToken);
                _stream = _tcpClient.GetStream();
            }

            if (_stream == null)
            {
                return (false, "Cannot open network stream to the server.");
            }

            var loginMsg = new Message
            {
                Type = MessageType.LOGIN,
                Sender = username.Trim(),
                Content = password,
                Timestamp = DateTime.UtcNow
            };

            lock (_sendLock)
            {
                FrameWriter.WriteMessage(_stream, loginMsg);
            }

            var response = await FrameReader.ReadMessageAsync(_stream, cancellationToken);
            if (response == null)
            {
                Disconnect();
                return (false, "Server closed the connection.");
            }

            if (response.Type == MessageType.LOGIN_OK)
            {
                CurrentUser = response.Sender.Length > 0 ? response.Sender : username.Trim();
                OnLoginSucceeded?.Invoke(CurrentUser);
                return (true, response.Content.Length > 0 ? response.Content : "Login successful!");
            }
            else if (response.Type == MessageType.ERROR)
            {
                OnLoginFailed?.Invoke(response.Content);
                return (false, response.Content);
            }
            else
            {
                string msg = $"Unexpected response: {response.Type}";
                OnLoginFailed?.Invoke(msg);
                return (false, msg);
            }
        }
        catch (SocketException)
        {
            Disconnect();
            return (false, $"Unable to connect to server ({host}:{port}). Please ensure ChatServer is running!");
        }
        catch (Exception ex)
        {
            Disconnect();
            return (false, $"Connection error: {ex.Message}");
        }
    }

    public void StartMessageLoop()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        _listenCts = new CancellationTokenSource();
        var token = _listenCts.Token;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && _stream != null)
                {
                    var msg = await FrameReader.ReadMessageAsync(_stream, token);
                    if (msg == null) break;

                    if (msg.Type == MessageType.FORCE_LOGOUT)
                    {
                        string reason = string.IsNullOrWhiteSpace(msg.Content)
                            ? "Your account has been logged in from another device."
                            : msg.Content;

                        Disconnect();
                        OnForceLogout?.Invoke(reason);
                        return;
                    }

                    // Raw dispatch
                    OnMessageReceived?.Invoke(msg);

                    // Strongly typed dispatch
                    DispatchTypedMessage(msg);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on clean disconnect
            }
            catch (Exception)
            {
                // Stream closed or error
            }
            finally
            {
                Disconnect();
            }
        }, token);
    }

    private void DispatchTypedMessage(Message msg)
    {
        try
        {
            switch (msg.Type)
            {
                case MessageType.GROUP_LIST:
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        var groups = JsonSerializer.Deserialize<List<GroupDto>>(msg.Content);
                        if (groups != null) OnGroupListReceived?.Invoke(groups);
                    }
                    break;

                case MessageType.CREATE_GROUP:
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        var createdGroup = JsonSerializer.Deserialize<GroupDto>(msg.Content);
                        if (createdGroup != null) OnGroupCreated?.Invoke(createdGroup);
                    }
                    break;

                case MessageType.MEMBER_LIST:
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        var members = JsonSerializer.Deserialize<List<GroupMemberDto>>(msg.Content);
                        if (members != null) OnMemberListReceived?.Invoke(msg.GroupId, members);
                    }
                    break;

                case MessageType.MESSAGE_HISTORY:
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        var history = JsonSerializer.Deserialize<List<MessageHistoryItemDto>>(msg.Content);
                        if (history != null) OnMessageHistoryReceived?.Invoke(msg.GroupId, history);
                    }
                    break;

                case MessageType.MESSAGE:
                    OnChatMessageReceived?.Invoke(msg);
                    break;

                case MessageType.ONLINE_LIST:
                    if (msg.Users != null)
                    {
                        OnOnlineListReceived?.Invoke(msg.Users);
                    }
                    break;

                case MessageType.USER_ONLINE:
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        OnUserOnline?.Invoke(msg.Content);
                    }
                    break;

                case MessageType.USER_OFFLINE:
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        OnUserOffline?.Invoke(msg.Content);
                    }
                    break;

                case MessageType.USER_LIST:
                    if (msg.Users != null)
                    {
                        OnUserListReceived?.Invoke(msg.Users);
                    }
                    break;

                case MessageType.ERROR:
                    OnErrorMessageReceived?.Invoke(msg.Content);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISPATCH ERROR] {ex.Message}");
        }
    }

    public async Task<bool> SendMessageAsync(Guid groupId, string content)
    {
        if (_stream == null || CurrentUser == null)
        {
            return false;
        }

        var chatMessage = new Message
        {
            Type = MessageType.MESSAGE,
            GroupId = groupId,
            Sender = CurrentUser,
            Content = content,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await Task.Run(() => Send(chatMessage));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Send message error: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    public void RequestGroupList()
    {
        Send(new Message
        {
            Type = MessageType.GROUP_LIST,
            Sender = CurrentUser ?? "",
            Timestamp = DateTime.UtcNow
        });
    }

    public void CreateGroup(string groupName, List<string>? memberUsernames = null)
    {
        Send(new Message
        {
            Type = MessageType.CREATE_GROUP,
            Sender = CurrentUser ?? "",
            Content = groupName.Trim(),
            Users = memberUsernames,
            Timestamp = DateTime.UtcNow
        });
    }

    public void AddMember(Guid groupId, string username)
    {
        Send(new Message
        {
            Type = MessageType.ADD_MEMBER,
            GroupId = groupId,
            Sender = CurrentUser ?? "",
            Content = username.Trim(),
            Timestamp = DateTime.UtcNow
        });
    }

    public void OpenGroup(Guid groupId)
    {
        Send(new Message
        {
            Type = MessageType.OPEN_GROUP,
            GroupId = groupId,
            Sender = CurrentUser ?? "",
            Timestamp = DateTime.UtcNow
        });
    }

    public void RequestUserList()
    {
        Send(new Message
        {
            Type = MessageType.USER_LIST,
            Sender = CurrentUser ?? "",
            Timestamp = DateTime.UtcNow
        });
    }

    public void Disconnect()
    {
        try
        {
            _listenCts?.Cancel();
            _listenCts?.Dispose();
            _listenCts = null;

            _stream?.Close();
            _tcpClient?.Close();
        }
        catch
        {
        }
        finally
        {
            _stream = null;
            _tcpClient = null;
            CurrentUser = null;
            OnDisconnected?.Invoke();
        }
    }

    public NetworkStream? GetStream() => _stream;
    public TcpClient? GetTcpClient() => _tcpClient;
}
