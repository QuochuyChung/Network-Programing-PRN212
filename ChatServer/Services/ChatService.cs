using ChatProtocol;
using ChatServer.Data;
using ChatServer.Data.Models;

namespace ChatServer.Services;

public class ChatService
{
    private readonly ClientHandler _client;

    public ChatService(ClientHandler client)
    {
        _client = client;
    }

    public void HandleChatMessage(Message message)
    {
        // 1. Luu tin nhan vao DB
        using var db = new ChatDbContext();
        db.Messages.Add(new MessageRecord
        {
            Id = Guid.NewGuid(),
            GroupId = message.GroupId,
            Sender = message.Sender,
            Content = message.Content,
            SentAt = message.Timestamp
        });
        db.SaveChanges();

        // 2. Lay danh sach username thuoc nhom nay (JOIN qua navigation property)
        List<string> memberUsernames = db.GroupMembers
            .Where(gm => gm.GroupId == message.GroupId)
            .Select(gm => gm.User.Username)
            .ToList();

        // 3. Doi chieu voi Online Registry - chi lock luc doc, khong lock luc gui
        // (gui qua socket co the cham, giu lock luc do se lam nghen thread khac)
        List<ClientHandler> targets = new();
        lock (ClientHandler.Lock)
        {
            foreach (string username in memberUsernames)
            {
                // lấy đối chiếu
                // OnlineUser[user từ list db - memberUsernames.username] = handler
                if (ClientHandler.OnlineUsers.TryGetValue(username, out var handler))
                targets.Add(handler);
            }
        }

        // 4. Gui cho tung nguoi dang online - lam ngoai lock
        foreach (var handler in targets)
        {
            handler.Send(message);
        }

        string preview = message.Content?.Length > 100
            ? message.Content.Substring(0, 100) + $"... ({message.Content.Length} chars)"
            : (message.Content ?? "");
        Console.WriteLine($"[{message.GroupId}] {message.Sender}: {preview}");
    }
}
