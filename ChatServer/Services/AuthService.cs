using ChatProtocol;

namespace ChatServer.Services;

public class AuthService
{
    private readonly ClientHandler _client;

    public AuthService(ClientHandler client)
    {
        _client = client;
    }

    public void HandleLogin(Message message)
    {
        _client.Username = message.Sender;

        lock (ClientHandler.Lock)
        {
            ClientHandler.OnlineUsers[_client.Username] = _client;
        }

        _client.Send(new Message { Type = MessageType.LOGIN_OK, Sender = "server" });
        Console.WriteLine($"{_client.Username} da online.");
    }
}
