using System.IO;
using System.Net.Sockets;
using ChatProtocol;

namespace ChatClient.Services;

/// <summary>
/// Quản lý duy nhất một kết nối TCP trong suốt phiên đăng nhập của client.
/// </summary>
public sealed class ChatClientService : IAsyncDisposable
{
    private static readonly Lazy<ChatClientService> LazyInstance = new(() => new ChatClientService());
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _connectionCancellation;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private Task? _receiveTask;
    private int _disconnectRaised;

    private ChatClientService()
    {
    }

    public static ChatClientService Instance => LazyInstance.Value;
    public bool IsConnected => _tcpClient?.Connected == true && _stream is not null;
    public string? CurrentUser { get; private set; }
    public string Username => CurrentUser ?? string.Empty;

    public event Action<string>? OnLoginSucceeded;
    public event Action<string>? OnLoginFailed;
    public event Action? OnDisconnected;
    public event Action<Message>? MessageReceived;
    public event Action<string>? Disconnected;

    public async Task<(bool Success, string Message)> ConnectAndLoginAsync(
        string host,
        int port,
        string username,
        string password = "",
        CancellationToken cancellationToken = default)
    {
        username = username.Trim();
        if (username.Length is < 1 or > 50)
        {
            return (false, "Username phải có từ 1 đến 50 ký tự.");
        }

        await DisconnectAsync(false);
        _connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Interlocked.Exchange(ref _disconnectRaised, 0);

        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, _connectionCancellation.Token);
            _stream = _tcpClient.GetStream();

            await SendAsync(new Message
            {
                Type = MessageType.LOGIN,
                Sender = username,
                Content = password,
                Timestamp = DateTime.UtcNow
            });

            using var loginTimeout = CancellationTokenSource.CreateLinkedTokenSource(_connectionCancellation.Token);
            loginTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var response = await FrameReader.ReadAsync(_stream, loginTimeout.Token);

            if (response?.Type == MessageType.LOGIN_OK)
            {
                CurrentUser = string.IsNullOrWhiteSpace(response.Sender) ? username : response.Sender;
                OnLoginSucceeded?.Invoke(CurrentUser);
                _receiveTask = ReceiveLoopAsync(_connectionCancellation.Token);
                return (true, string.IsNullOrWhiteSpace(response.Content)
                    ? "Đăng nhập thành công."
                    : response.Content);
            }

            string error = response?.Content ?? "Server đã đóng kết nối.";
            OnLoginFailed?.Invoke(error);
            await DisconnectAsync(false);
            return (false, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await DisconnectAsync(false);
            return (false, "Kết nối hoặc đăng nhập đã hết thời gian chờ.");
        }
        catch (SocketException)
        {
            await DisconnectAsync(false);
            return (false, $"Không thể kết nối tới server ({host}:{port}). Hãy kiểm tra ChatServer đang chạy.");
        }
        catch (Exception exception)
        {
            await DisconnectAsync(false);
            return (false, $"Lỗi kết nối: {exception.Message}");
        }
    }

    public Task SendAsync(Message message)
    {
        if (_stream is null || _connectionCancellation is null || _connectionCancellation.IsCancellationRequested)
        {
            throw new InvalidOperationException("Chưa kết nối tới server.");
        }

        return FrameWriter.WriteAsync(_stream, message, _writeLock, _connectionCancellation.Token);
    }

    public async Task<bool> SendMessageAsync(Guid groupId, string content)
    {
        if (groupId == Guid.Empty || string.IsNullOrWhiteSpace(content) || CurrentUser is null)
        {
            return false;
        }

        try
        {
            await SendAsync(new Message
            {
                Type = MessageType.MESSAGE,
                GroupId = groupId,
                Sender = CurrentUser,
                Content = content.Trim(),
                Timestamp = DateTime.UtcNow
            });
            return true;
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            await DisconnectAsync(true, $"Mất kết nối tới server: {exception.Message}");
            return false;
        }
    }

    public void Disconnect() => _ = DisconnectAsync(true, waitForReceiveTask: false);

    public NetworkStream? GetStream() => _stream;
    public TcpClient? GetTcpClient() => _tcpClient;

    public async ValueTask DisposeAsync() => await DisconnectAsync(false);

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                var message = await FrameReader.ReadAsync(_stream, cancellationToken);
                if (message is null)
                {
                    await DisconnectAsync(true, "Server đã đóng kết nối.", waitForReceiveTask: false);
                    return;
                }

                MessageReceived?.Invoke(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            await DisconnectAsync(
                true,
                $"Mất kết nối tới server: {exception.Message}",
                waitForReceiveTask: false);
        }
    }

    private async Task DisconnectAsync(
        bool notify,
        string reason = "Đã ngắt kết nối.",
        bool waitForReceiveTask = true)
    {
        var cancellation = _connectionCancellation;
        var receiveTask = _receiveTask;

        _connectionCancellation = null;
        _receiveTask = null;
        _stream = null;
        CurrentUser = null;

        if (cancellation is not null)
        {
            await cancellation.CancelAsync();
        }

        _tcpClient?.Dispose();
        _tcpClient = null;

        if (waitForReceiveTask && receiveTask is not null && Task.CurrentId != receiveTask.Id)
        {
            try
            {
                await receiveTask;
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        cancellation?.Dispose();

        if (notify && Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
        {
            Disconnected?.Invoke(reason);
            OnDisconnected?.Invoke();
        }
    }
}
