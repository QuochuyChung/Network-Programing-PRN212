using System.IO;
using System.Net.Sockets;
using ChatProtocol;

namespace ChatClient.Services;

public class ChatClientService
{
    private static ChatClientService? _instance;
    public static ChatClientService Instance => _instance ??= new ChatClientService();

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCancellation;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disconnectRaised;

    public bool IsConnected => _tcpClient?.Connected == true;
    public string? CurrentUser { get; private set; }

    public event Action<string>? OnLoginSucceeded;
    public event Action<string>? OnLoginFailed;
    public event Action<Message>? OnMessageReceived;
    public event Action? OnDisconnected;

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
                _disconnectRaised = false;
                await _tcpClient.ConnectAsync(host, port, cancellationToken);
                _stream = _tcpClient.GetStream();
            }

            if (_stream == null)
            {
                return (false, "Cannot open network stream to the server.");
            }

            // Gửi gói tin LOGIN theo chuẩn ChatProtocol của nhóm
            var loginMsg = new Message
            {
                Type = MessageType.LOGIN,
                Sender = username.Trim(),
                Content = password,
                Timestamp = DateTime.UtcNow
            };

            FrameWriter.WriteMessage(_stream, loginMsg);

            // Đọc phản hồi từ server
            var response = await Task.Run(() => FrameReader.ReadMessage(_stream), cancellationToken);
            if (response == null)
            {
                Disconnect();
                return (false, "Server closed the connection.");
            }

            if (response.Type == MessageType.LOGIN_OK)
            {
                CurrentUser = response.Sender.Length > 0 ? response.Sender : username.Trim();
                OnLoginSucceeded?.Invoke(CurrentUser);
                StartReceiveLoop();
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

    public async Task<bool> SendMessageAsync(Guid groupId, string content)
    {
        if (_stream == null || CurrentUser == null)
        {
            return false; // chua connect/chua login thi khong gui duoc
        }

        var chatMessage = new Message
        {
            Type = MessageType.MESSAGE,
            GroupId = groupId,
            Sender = CurrentUser,
            Content = content,
            Timestamp = DateTime.UtcNow
        };

        return await SendAsync(chatMessage);
    }

    public Task<bool> RequestGroupListAsync()
    {
        return SendAsync(new Message
        {
            Type = MessageType.GROUP_LIST,
            Sender = CurrentUser ?? string.Empty,
            Timestamp = DateTime.UtcNow
        });
    }

    public Task<bool> CreateGroupAsync(string groupName)
    {
        return SendAsync(new Message
        {
            Type = MessageType.CREATE_GROUP,
            Sender = CurrentUser ?? string.Empty,
            Content = groupName.Trim(),
            Timestamp = DateTime.UtcNow
        });
    }

    public Task<bool> AddMemberAsync(Guid groupId, string username)
    {
        return SendAsync(new Message
        {
            Type = MessageType.ADD_MEMBER,
            GroupId = groupId,
            Sender = CurrentUser ?? string.Empty,
            Content = username.Trim(),
            Timestamp = DateTime.UtcNow
        });
    }

    public Task<bool> OpenGroupAsync(Guid groupId)
    {
        return SendAsync(new Message
        {
            Type = MessageType.OPEN_GROUP,
            GroupId = groupId,
            Sender = CurrentUser ?? string.Empty,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task<bool> SendAsync(Message message)
    {
        NetworkStream? stream = _stream;
        if (stream == null || CurrentUser == null) return false;

        await _sendLock.WaitAsync();
        try
        {
            // Mọi request dùng chung một TCP stream nên phải ghi tuần tự.
            await Task.Run(() => FrameWriter.WriteMessage(stream, message));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Send message error: {ex.Message}");
            Disconnect();
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void StartReceiveLoop()
    {
        _receiveCancellation?.Cancel();
        _receiveCancellation?.Dispose();
        _receiveCancellation = new CancellationTokenSource();
        CancellationToken token = _receiveCancellation.Token;

        // Một reader nền duy nhất nhận mọi phản hồi sau LOGIN_OK rồi phát event cho UI.
        _ = Task.Run(() => ReceiveLoop(token), token);
    }

    private void ReceiveLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _stream != null)
            {
                Message? message = FrameReader.ReadMessage(_stream);
                if (message == null) break;
                OnMessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Đóng app hoặc mất mạng đều kết thúc reader tại đây.
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                Disconnect();
            }
        }
    }

    public void Disconnect()
    {
        bool shouldNotify = !_disconnectRaised;
        _disconnectRaised = true;
        _receiveCancellation?.Cancel();

        try
        {
            _stream?.Close();
            _tcpClient?.Close();
        }
        catch
        {
            // Ignore socket cleanup exceptions
        }
        finally
        {
            _stream = null;
            _tcpClient = null;
            CurrentUser = null;
            if (shouldNotify) OnDisconnected?.Invoke();
        }
    }

    public NetworkStream? GetStream() => _stream;
    public TcpClient? GetTcpClient() => _tcpClient;
}
