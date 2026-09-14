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
                // lấy value là handler, handler là hàm là action
                    handler(message);
                else
                    Console.WriteLine($"Chua co handler cho {message.Type}");
            }
        }
        catch (IOException)
        {
        }
        // chat xong hay là xong request thì disconnect lại
        finally
        {
            Disconnect();
        }
    }

    public void Send(Message message) => FrameWriter.WriteMessage(_stream, message);

    private void Disconnect()
    {
        lock (Lock)
        {
            if (Username != "") OnlineUsers.Remove(Username);
        }
        _client.Close();
        Console.WriteLine($"{Username} da ngat ket noi.");
    }
}
