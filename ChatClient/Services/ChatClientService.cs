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

    public void Disconnect()
    {
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
            OnDisconnected?.Invoke();
        }
    }

    public NetworkStream? GetStream() => _stream;
    public TcpClient? GetTcpClient() => _tcpClient;
}
