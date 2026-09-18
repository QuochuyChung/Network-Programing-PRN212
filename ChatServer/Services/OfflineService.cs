using ChatProtocol;

namespace ChatServer.Services;

public class OfflineService
{
    private readonly ClientHandler _client;

    public OfflineService(ClientHandler client)
    {
        _client = client;
        _client.OnDisconnect += HandleOffline;
    }

    public void HandleOffline()
    {
        List<ClientHandler> targets;
        lock (ClientHandler.Lock)
        {
            targets = ClientHandler.OnlineUsers.Values.Where(h => h.Username != _client.Username).ToList();
        }

        var message = new Message
        {
            Type = MessageType.USER_OFFLINE,
            Sender = "server",
            Content = _client.Username
        };

        foreach (var handler in targets)
        {
            try { handler.Send(message); }
            catch { }
        }
    }
}
