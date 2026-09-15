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

        // 1. Check or create the user in the database
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

            // 2. Check if user already has an active session on another device; if so, kick it
            ClientHandler? oldSession = null;
            lock (ClientHandler.Lock)
            {
                if (ClientHandler.OnlineUsers.TryGetValue(_client.Username, out oldSession) && oldSession != _client)
                {
                    ClientHandler.OnlineUsers.Remove(_client.Username);
                }
                ClientHandler.OnlineUsers[_client.Username] = _client;
            }

            if (oldSession != null && oldSession != _client)
            {
                Console.WriteLine($"[SESSION REPLACED] User '{_client.Username}' logged in from another device. Kicking previous session.");
                oldSession.Kick("Your account has been logged in from another device.");
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
