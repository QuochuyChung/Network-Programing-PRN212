using ChatProtocol;
using ChatServer.Data;
using ChatServer.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChatServer.Services;

public class ChatService
{
    private readonly ClientHandler _client;

    public ChatService(ClientHandler client)
    {
        _client = client;
    }

    public void HandleGroupList(Message message)
    {
        if (!EnsureLoggedIn()) return;

        try
        {
            using var db = new ChatDbContext();
            SendGroupList(db, _client);
        }
        catch (Exception ex)
        {
            SendDatabaseError("load the group list", ex);
        }
    }

    public void HandleCreateGroup(Message message)
    {
        if (!EnsureLoggedIn()) return;

        string groupName = message.Content.Trim();
        if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > 100)
        {
            SendError("Group name must contain 1 to 100 characters.");
            return;
        }

        try
        {
            using var db = new ChatDbContext();
            var creator = db.Users.FirstOrDefault(u => u.Username == _client.Username);
            if (creator == null)
            {
                SendError("The current user no longer exists in the database.");
                return;
            }

            // Tạo group và thêm người tạo làm thành viên trong cùng một lần SaveChanges.
            var group = new Group
            {
                Id = Guid.NewGuid(),
                Name = groupName,
                CreatedAt = DateTime.UtcNow
            };
            db.Groups.Add(group);
            db.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                UserId = creator.Id,
                JoinedAt = DateTime.UtcNow
            });
            db.SaveChanges();

            _client.Send(new Message
            {
                Type = MessageType.CREATE_GROUP,
                GroupId = group.Id,
                Sender = "server",
                Content = $"Group '{group.Name}' was created successfully.",
                Timestamp = DateTime.UtcNow
            });

            // Trả danh sách mới ngay để UI không cần tự đoán state vừa tạo.
            SendGroupList(db, _client);
            Console.WriteLine($"[GROUP CREATED] {_client.Username} created '{group.Name}' ({group.Id}).");
        }
        catch (Exception ex)
        {
            SendDatabaseError("create the group", ex);
        }
    }

    public void HandleAddMember(Message message)
    {
        if (!EnsureLoggedIn()) return;

        string memberUsername = message.Content.Trim();
        if (message.GroupId == Guid.Empty || string.IsNullOrWhiteSpace(memberUsername))
        {
            SendError("Please select a group and enter the username to add.");
            return;
        }

        try
        {
            using var db = new ChatDbContext();

            // Chỉ thành viên hiện tại của group mới được mời thêm người khác.
            bool requesterIsMember = db.GroupMembers.Any(gm =>
                gm.GroupId == message.GroupId && gm.User.Username == _client.Username);
            if (!requesterIsMember)
            {
                SendError("You are not a member of this group.");
                return;
            }

            var group = db.Groups.FirstOrDefault(g => g.Id == message.GroupId);
            var member = db.Users.FirstOrDefault(u => u.Username.ToLower() == memberUsername.ToLower());
            if (group == null)
            {
                SendError("The selected group does not exist.");
                return;
            }
            if (member == null)
            {
                SendError($"User '{memberUsername}' does not exist. That user must log in once before being added.");
                return;
            }
            if (db.GroupMembers.Any(gm => gm.GroupId == group.Id && gm.UserId == member.Id))
            {
                SendError($"User '{member.Username}' is already in this group.");
                return;
            }

            db.GroupMembers.Add(new GroupMember
            {
                GroupId = group.Id,
                UserId = member.Id,
                JoinedAt = DateTime.UtcNow
            });
            db.SaveChanges();

            _client.Send(new Message
            {
                Type = MessageType.ADD_MEMBER,
                GroupId = group.Id,
                Sender = "server",
                Content = $"Added '{member.Username}' to '{group.Name}'.",
                Timestamp = DateTime.UtcNow
            });

            // Nếu người vừa được thêm đang online, đẩy danh sách group mới cho họ ngay.
            ClientHandler? onlineMember;
            lock (ClientHandler.Lock)
            {
                ClientHandler.OnlineUsers.TryGetValue(member.Username, out onlineMember);
            }
            if (onlineMember != null && onlineMember != _client)
            {
                SendGroupList(db, onlineMember);
            }

            // Cập nhật member count trên danh sách của người thao tác.
            SendGroupList(db, _client);
            SendMemberList(db, group.Id, _client);
            Console.WriteLine($"[MEMBER ADDED] {_client.Username} added '{member.Username}' to '{group.Name}'.");
        }
        catch (Exception ex)
        {
            SendDatabaseError("add the member", ex);
        }
    }

    public void HandleOpenGroup(Message message)
    {
        if (!EnsureLoggedIn()) return;
        if (message.GroupId == Guid.Empty)
        {
            SendError("Please select a valid group.");
            return;
        }

        try
        {
            using var db = new ChatDbContext();

            // Không cho client đọc danh sách thành viên của group mà họ không tham gia.
            bool requesterIsMember = db.GroupMembers.Any(gm =>
                gm.GroupId == message.GroupId && gm.User.Username == _client.Username);
            if (!requesterIsMember)
            {
                SendError("You are not a member of this group.");
                return;
            }

            SendMemberList(db, message.GroupId, _client);
        }
        catch (Exception ex)
        {
            SendDatabaseError("open the group", ex);
        }
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

        Console.WriteLine($"[{message.GroupId}] {message.Sender}: {message.Content}");
    }

    private void SendGroupList(ChatDbContext db, ClientHandler target)
    {
        // Chỉ lấy các group mà chính user đang đăng nhập là thành viên.
        var groups = db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.User.Username == target.Username)
            .OrderByDescending(gm => gm.Group.CreatedAt)
            .Select(gm => new
            {
                Id = gm.GroupId,
                gm.Group.Name,
                MemberCount = db.GroupMembers.Count(member => member.GroupId == gm.GroupId)
            })
            .ToList();

        target.Send(new Message
        {
            Type = MessageType.GROUP_LIST,
            Sender = "server",
            Content = JsonSerializer.Serialize(groups),
            Timestamp = DateTime.UtcNow
        });
    }

    private static void SendMemberList(ChatDbContext db, Guid groupId, ClientHandler target)
    {
        var memberNames = db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.GroupId == groupId)
            .OrderBy(gm => gm.User.Username)
            .Select(gm => gm.User.Username)
            .ToList();

        // Online Registry là dữ liệu RAM nên lấy snapshot ngắn bên trong lock,
        // sau đó mới dựng payload và gửi qua socket ở bên ngoài lock.
        HashSet<string> onlineUsers;
        lock (ClientHandler.Lock)
        {
            onlineUsers = ClientHandler.OnlineUsers.Keys
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var members = memberNames.Select(username => new
        {
            Username = username,
            IsOnline = onlineUsers.Contains(username)
        });

        target.Send(new Message
        {
            Type = MessageType.MEMBER_LIST,
            GroupId = groupId,
            Sender = "server",
            Content = JsonSerializer.Serialize(members),
            Timestamp = DateTime.UtcNow
        });
    }

    private bool EnsureLoggedIn()
    {
        if (!string.IsNullOrWhiteSpace(_client.Username)) return true;

        SendError("Please log in before using group features.");
        return false;
    }

    private void SendError(string content)
    {
        _client.Send(new Message
        {
            Type = MessageType.ERROR,
            Sender = "server",
            Content = content,
            Timestamp = DateTime.UtcNow
        });
    }

    private void SendDatabaseError(string operation, Exception exception)
    {
        Console.WriteLine($"[DB ERROR] Could not {operation}: {exception.Message}");
        SendError($"Database error: could not {operation}.");
    }
}
