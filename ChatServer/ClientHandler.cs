using System.Net.Sockets;
using ChatProtocol;
using ChatServer.Services;

public class ClientHandler
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Dictionary<MessageType, Action<Message>> _handlers;

    public static readonly Dictionary<string, ClientHandler> OnlineUsers = new();
    public static readonly object Lock = new();

    public string Username { get; set; } = "";

    public ClientHandler(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();

        var authService = new AuthService(this);
        var chatService = new ChatService(this);

        _handlers = new Dictionary<MessageType, Action<Message>>
        {
            { MessageType.LOGIN, authService.HandleLogin },
            { MessageType.MESSAGE, chatService.HandleChatMessage }
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

    public void Send(Message message) => FrameWriter.WriteMessage(_stream, message);

    public void SendOnlineList()
    {
        List<string> onlineUsernames;
        lock (Lock)
        {
            onlineUsernames = OnlineUsers.Keys.Where(u => u != Username).ToList();
        }

        Send(new Message
        {
            Type = MessageType.ONLINE_LIST,
            Sender = "server",
            Users = onlineUsernames
        });
    }

    public void BroadcastOnline()
    {
        BroadcastExcept(new Message
        {
            Type = MessageType.USER_ONLINE,
            Sender = "server",
            Content = Username
        });
    }

    public void BroadcastOffline()
    {
        BroadcastExcept(new Message
        {
            Type = MessageType.USER_OFFLINE,
            Sender = "server",
            Content = Username
        });
    }

    private void BroadcastExcept(Message message)
    {
        List<ClientHandler> targets;
        lock (Lock)
        {
            targets = OnlineUsers.Values.Where(h => h.Username != Username).ToList();
        }

        foreach (var handler in targets)
        {
            try { handler.Send(message); }
            catch { }
        }
    }

    private void Disconnect()
    {
        lock (Lock)
        {
            if (Username != "") OnlineUsers.Remove(Username);
        }

        if (Username != "") BroadcastOffline();

        _client.Close();
        Console.WriteLine($"{Username} da ngat ket noi.");
    }
}
