using ChatProtocol;
using ChatServer.Data;
using ChatServer.Data.Models;

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
        string username = message.Sender?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(username))
        {
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = "Please enter your username."
            });
            return;
        }

        // 1. Check if the username is already online
        lock (ClientHandler.Lock)
        {
            if (ClientHandler.OnlineUsers.ContainsKey(username))
            {
                Console.WriteLine($"[LOGIN REJECTED] Username '{username}' is already online.");
                _client.Send(new Message
                {
                    Type = MessageType.ERROR,
                    Sender = "server",
                    Content = $"Username '{username}' is already online on another device."
                });
                return;
            }
        }

        // 2. Check or create the user in the database
        try
        {
            using var db = new ChatDbContext();
            var user = db.Users.FirstOrDefault(u => u.Username.ToLower() == username.ToLower());

            if (user == null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Username = username,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
                db.SaveChanges();
                Console.WriteLine($"[DB] Auto-registered new user '{username}'.");
            }

            _client.Username = user.Username;

            lock (ClientHandler.Lock)
            {
                ClientHandler.OnlineUsers[_client.Username] = _client;
            }

            _client.Send(new Message
            {
                Type = MessageType.LOGIN_OK,
                Sender = user.Username,
                Content = $"Welcome to ChatApp, {user.Username}!"
            });

            Console.WriteLine($"[ONLINE] {user.Username} logged in successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB ERROR] Failed during user lookup/creation: {ex.Message}");
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = "Database connection error. Unable to verify or register user."
            });
        }
    }
}
