using System.Net.Sockets;
using ChatProtocol;
using ChatServer.Services;

public class ClientHandler
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Dictionary<MessageType, Action<Message>> _handlers;

    public static readonly Dictionary<string, ClientHandler> OnlineUsers = new(StringComparer.OrdinalIgnoreCase);
    public static readonly object Lock = new();

    private bool _isDisconnected = false;
    private readonly object _sendLock = new();

    public string Username { get; set; } = "";

    public event Action? OnDisconnect;
    public event Action<Message>? OnSend;

    public ClientHandler(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();

        var authService = new AuthService(this);
        var chatService = new ChatService(this);
        var onlineService = new OnlineService(this);
        var offlineService = new OfflineService(this);
        var groupService = new GroupService(this);

        _handlers = new Dictionary<MessageType, Action<Message>>
        {
            { MessageType.LOGIN, authService.HandleLogin },
            { MessageType.MESSAGE, chatService.HandleChatMessage },
            { MessageType.GROUP_LIST, groupService.HandleGroupList },
            { MessageType.CREATE_GROUP, groupService.HandleCreateGroup },
            { MessageType.ADD_MEMBER, groupService.HandleAddMember },
            { MessageType.OPEN_GROUP, groupService.HandleOpenGroup },
            { MessageType.USER_LIST, groupService.HandleUserList }
        };
    }

    public void Run()
    {
        try
        {
            while (true)
            {
                Message? message = FrameReader.ReadMessage(_stream);
                if (message == null) break;

                if (_handlers.TryGetValue(message.Type, out var handler))
                    handler(message);
                else
                    Console.WriteLine($"Chua co handler cho {message.Type}");
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            Disconnect();
        }
    }

    public void Send(Message message)
    {
        lock (_sendLock)
        {
            try
            {
                FrameWriter.WriteMessage(_stream, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEND ERROR] Failed sending to {Username}: {ex.Message}");
                return;
            }
        }
        OnSend?.Invoke(message);
    }

    public void Kick(string reason)
    {
        lock (_sendLock)
        {
            try
            {
                FrameWriter.WriteMessage(_stream, new Message
                {
                    Type = MessageType.FORCE_LOGOUT,
                    Sender = "server",
                    Content = reason,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KICK ERROR] Failed to send FORCE_LOGOUT to {Username}: {ex.Message}");
            }
        }

        // TCP Graceful Shutdown: đóng chiều Send để gửi cờ FIN tới client.
        // Client sẽ nhận trọn vẹn gói tin FORCE_LOGOUT rồi tự ngắt kết nối.
        try
        {
            if (_client.Connected)
            {
                _client.Client.Shutdown(SocketShutdown.Send);
            }
        }
        catch { }

        // Watchdog dự phòng dọn dẹp nếu client bị treo hoặc mất kết nối mạng đột ngột
        Task.Run(async () =>
        {
            await Task.Delay(3000);
            Disconnect();
        });
    }

    public void Disconnect()
    {
        lock (Lock)
        {
            if (_isDisconnected) return;
            _isDisconnected = true;

            if (!string.IsNullOrEmpty(Username) && OnlineUsers.TryGetValue(Username, out var current) && current == this)
            {
                OnlineUsers.Remove(Username);
            }
        }

        OnDisconnect?.Invoke();

        try
        {
            _stream.Close();
            _client.Close();
        }
        catch { }
        Console.WriteLine($"{Username} da ngat ket noi.");
    }
}
