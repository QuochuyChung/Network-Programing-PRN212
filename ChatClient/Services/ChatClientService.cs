using System.Net.Sockets;
using ChatProtocol;

namespace ChatClient.Services;

public class ChatClientService
{
    private static ChatClientService? _instance;
    public static ChatClientService Instance => _instance ??= new ChatClientService();

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public bool IsConnected => _tcpClient?.Connected == true;
    public string? CurrentUser { get; private set; }

    public event Action<string>? OnLoginSucceeded;
    public event Action<string>? OnLoginFailed;
    public event Action? OnDisconnected;
    public event Action<string>? OnForceLogout;
    public event Action<Message>? OnMessageReceived;

    private readonly object _sendLock = new();
    private CancellationTokenSource? _listenCts;

    public void Send(Message message)
    {
        lock (_sendLock)
        {
            if (_stream != null)
            {
                FrameWriter.WriteMessage(_stream, message);
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

            // Gửi gói tin LOGIN theo chuẩn ChatProtocol của nhóm (đồng bộ luồng bằng lock)
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

            // Đọc phản hồi từ server bất đồng bộ (giải phóng thread)
            var response = await FrameReader.ReadMessageAsync(_stream, cancellationToken);
            if (response == null)
            {
                Disconnect();
                return (false, "Server closed the connection.");
            }

            if (response.Type == MessageType.LOGIN_OK)
            {
                CurrentUser = response.Sender.Length > 0 ? response.Sender : username.Trim();
                StartMessageLoop();
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

    private void StartMessageLoop()
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
                    else
                    {
                        OnMessageReceived?.Invoke(msg);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected cancellation on logout
            }
            catch (Exception)
            {
                // Stream closed or connection terminated
            }
            finally
            {
                Disconnect();
            }
        }, token);
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

        try
        {
            await Task.Run(() => FrameWriter.WriteMessage(_stream, chatMessage));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Send message error: {ex.Message}");
            Disconnect();
            return false;
        }
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
            // Ignore socket cleanup exceptions
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
