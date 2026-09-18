using ChatProtocol;

namespace ChatServer.Services;

public class OnlineService
{
    private readonly ClientHandler _client;

    public OnlineService(ClientHandler client)
    {
        _client = client;
        _client.OnSend += HandleOnline;
    }

    private void HandleOnline(Message message)
    {
        if (message.Type != MessageType.LOGIN_OK) return;

        // Send full online list to the newly logged-in client
        List<string> onlineUsernames;
        lock (ClientHandler.Lock)
        {
            onlineUsernames = ClientHandler.OnlineUsers.Keys.Where(u => u != _client.Username).ToList();
        }

        _client.Send(new Message
        {
            Type = MessageType.ONLINE_LIST,
            Sender = "server",
            Users = onlineUsernames
        });

        // Broadcast USER_ONLINE to all other clients
        List<ClientHandler> targets;
        lock (ClientHandler.Lock)
        {
            targets = ClientHandler.OnlineUsers.Values.Where(h => h.Username != _client.Username).ToList();
        }

        var msg = new Message
        {
            Type = MessageType.USER_ONLINE,
            Sender = "server",
            Content = _client.Username
        };

        foreach (var handler in targets)
        {
            try { handler.Send(msg); }
            catch { }
        }
    }
}
