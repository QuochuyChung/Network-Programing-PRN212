using System.Collections.Concurrent;
using ChatProtocol;
using ChatServer.Data;
using ChatServer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatServer;

/// <summary>
/// Chứa online registry và toàn bộ nghiệp vụ nhóm để mọi ClientHandler dùng chung
/// một nguồn trạng thái trong RAM.
/// </summary>
public sealed class GroupManager
{
    private readonly ConcurrentDictionary<string, ClientHandler> _onlineUsers =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task LoginAsync(ClientHandler handler, string? rawUsername)
    {
        string username = rawUsername?.Trim() ?? string.Empty;
        if (username.Length is < 1 or > 50)
        {
            await handler.SendErrorAsync("Username phải có từ 1 đến 50 ký tự.");
            return;
        }

        if (!_onlineUsers.TryAdd(username, handler))
        {
            await handler.SendErrorAsync("Username này đang đăng nhập trên một thiết bị khác.");
            return;
        }

        try
        {
            await using var db = new ChatDbContext();
            var user = await db.Users.FirstOrDefaultAsync(item =>
                EF.Functions.ILike(item.Username, username));
            if (user is null)
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Username = username,
                    CreatedAt = DateTime.UtcNow
                };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            handler.Username = user.Username;
            await handler.SendAsync(new Message
            {
                Type = MessageType.LOGIN_OK,
                Sender = user.Username,
                Content = "Đăng nhập thành công."
            });

            await BroadcastPresenceAsync(user.Username, true, handler);
            Console.WriteLine($"{user.Username} đã đăng nhập.");
        }
        catch (Exception exception)
        {
            _onlineUsers.TryRemove(new KeyValuePair<string, ClientHandler>(username, handler));
            handler.Username = null;
            Console.Error.WriteLine($"Đăng nhập thất bại cho {username}: {exception}");
            await handler.SendErrorAsync(
                "Không thể đăng nhập do lỗi cơ sở dữ liệu. Hãy kiểm tra connection string và migration.");
        }
    }

    public async Task SendGroupListAsync(ClientHandler handler)
    {
        if (!TryGetUsername(handler, out string username))
        {
            return;
        }

        await using var db = new ChatDbContext();
        var groups = await db.GroupMembers
            .AsNoTracking()
            .Where(member => member.User.Username == username)
            .OrderBy(member => member.Group.Name)
            .Select(member => new GroupInfo
            {
                Id = member.GroupId,
                Name = member.Group.Name
            })
            .ToListAsync();

        await handler.SendAsync(new Message
        {
            Type = MessageType.GROUP_LIST,
            Groups = groups
        });
    }

    public async Task CreateGroupAsync(ClientHandler handler, string? rawGroupName)
    {
        if (!TryGetUsername(handler, out string username))
        {
            return;
        }

        string groupName = rawGroupName?.Trim() ?? string.Empty;
        if (groupName.Length is < 1 or > 100)
        {
            await handler.SendErrorAsync("Tên nhóm phải có từ 1 đến 100 ký tự.");
            return;
        }

        await using var db = new ChatDbContext();
        var user = await db.Users.SingleAsync(item => item.Username == username);
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
            UserId = user.Id,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await handler.SendAsync(new Message
        {
            Type = MessageType.CREATE_GROUP,
            GroupId = group.Id,
            GroupName = group.Name,
            Content = $"Đã tạo nhóm “{group.Name}”."
        });
        await SendGroupListAsync(handler);
    }

    public async Task AddMemberAsync(ClientHandler handler, Guid groupId, string? rawTargetUsername)
    {
        if (!TryGetUsername(handler, out string username))
        {
            return;
        }

        if (groupId == Guid.Empty)
        {
            await handler.SendErrorAsync("Bạn chưa chọn nhóm.");
            return;
        }

        string targetUsername = rawTargetUsername?.Trim() ?? string.Empty;
        if (targetUsername.Length is < 1 or > 50)
        {
            await handler.SendErrorAsync("Username cần thêm phải có từ 1 đến 50 ký tự.");
            return;
        }

        await using var db = new ChatDbContext();
        bool requesterIsMember = await db.GroupMembers.AnyAsync(member =>
            member.GroupId == groupId && member.User.Username == username);
        if (!requesterIsMember)
        {
            await handler.SendErrorAsync("Bạn không phải thành viên của nhóm này.");
            return;
        }

        var targetUser = await db.Users.FirstOrDefaultAsync(user =>
            EF.Functions.ILike(user.Username, targetUsername));
        if (targetUser is null)
        {
            await handler.SendErrorAsync(
                $"Không tìm thấy người dùng “{targetUsername}”. Người này cần đăng nhập ít nhất một lần.");
            return;
        }

        bool alreadyMember = await db.GroupMembers.AnyAsync(member =>
            member.GroupId == groupId && member.UserId == targetUser.Id);
        if (alreadyMember)
        {
            await handler.SendErrorAsync($"{targetUser.Username} đã ở trong nhóm.");
            return;
        }

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = groupId,
            UserId = targetUser.Id,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        string groupName = await db.Groups
            .Where(group => group.Id == groupId)
            .Select(group => group.Name)
            .SingleAsync();

        await handler.SendAsync(new Message
        {
            Type = MessageType.ADD_MEMBER,
            GroupId = groupId,
            GroupName = groupName,
            TargetUsername = targetUser.Username,
            Content = $"Đã thêm {targetUser.Username} vào nhóm."
        });

        if (_onlineUsers.TryGetValue(targetUser.Username, out var targetHandler))
        {
            await SendGroupListAsync(targetHandler);
        }

        await BroadcastMemberListAsync(groupId);
    }

    public async Task SendMemberListAsync(ClientHandler handler, Guid groupId)
    {
        if (!TryGetUsername(handler, out string username) || groupId == Guid.Empty)
        {
            await handler.SendErrorAsync("Nhóm không hợp lệ.");
            return;
        }

        await using var db = new ChatDbContext();
        bool isMember = await db.GroupMembers.AnyAsync(member =>
            member.GroupId == groupId && member.User.Username == username);
        if (!isMember)
        {
            await handler.SendErrorAsync("Bạn không phải thành viên của nhóm này.");
            return;
        }

        var memberNames = await db.GroupMembers
            .AsNoTracking()
            .Where(member => member.GroupId == groupId)
            .OrderBy(member => member.User.Username)
            .Select(member => member.User.Username)
            .ToListAsync();

        await handler.SendAsync(new Message
        {
            Type = MessageType.MEMBER_LIST,
            GroupId = groupId,
            Members = memberNames.Select(name => new MemberInfo
            {
                Username = name,
                IsOnline = _onlineUsers.ContainsKey(name)
            }).ToList()
        });
    }

    public async Task SendChatMessageAsync(ClientHandler handler, Message incoming)
    {
        if (!TryGetUsername(handler, out string username) ||
            incoming.GroupId == Guid.Empty ||
            string.IsNullOrWhiteSpace(incoming.Content))
        {
            await handler.SendErrorAsync("Tin nhắn hoặc nhóm không hợp lệ.");
            return;
        }

        await using var db = new ChatDbContext();
        bool isMember = await db.GroupMembers.AnyAsync(member =>
            member.GroupId == incoming.GroupId && member.User.Username == username);
        if (!isMember)
        {
            await handler.SendErrorAsync("Bạn không phải thành viên của nhóm này.");
            return;
        }

        var outgoing = new Message
        {
            Type = MessageType.MESSAGE,
            GroupId = incoming.GroupId,
            Sender = username,
            Content = incoming.Content.Trim(),
            Timestamp = DateTime.UtcNow
        };

        db.Messages.Add(new MessageRecord
        {
            Id = Guid.NewGuid(),
            GroupId = outgoing.GroupId,
            Sender = outgoing.Sender,
            Content = outgoing.Content,
            SentAt = outgoing.Timestamp
        });
        await db.SaveChangesAsync();

        var memberNames = await db.GroupMembers
            .Where(member => member.GroupId == outgoing.GroupId)
            .Select(member => member.User.Username)
            .ToListAsync();
        var targets = GetOnlineHandlers(memberNames);
        await Task.WhenAll(targets.Select(target => SafeSendAsync(target, outgoing)));
    }

    public async Task DisconnectAsync(ClientHandler handler)
    {
        string? username = handler.Username;
        if (username is null ||
            !_onlineUsers.TryRemove(new KeyValuePair<string, ClientHandler>(username, handler)))
        {
            return;
        }

        await BroadcastPresenceAsync(username, false, handler);
    }

    private async Task BroadcastMemberListAsync(Guid groupId)
    {
        await using var db = new ChatDbContext();
        var usernames = await db.GroupMembers
            .Where(member => member.GroupId == groupId)
            .Select(member => member.User.Username)
            .ToListAsync();

        await Task.WhenAll(GetOnlineHandlers(usernames)
            .Select(handler => SafeSendMemberListAsync(handler, groupId)));
    }

    private async Task BroadcastPresenceAsync(
        string username,
        bool isOnline,
        ClientHandler sourceHandler)
    {
        var update = new Message
        {
            Type = isOnline ? MessageType.USER_ONLINE : MessageType.USER_OFFLINE,
            Sender = username,
            Content = username
        };

        var targets = _onlineUsers.Values
            .Where(handler => !ReferenceEquals(handler, sourceHandler))
            .Distinct()
            .ToList();
        await Task.WhenAll(targets.Select(handler => SafeSendAsync(handler, update)));
    }

    private static async Task SafeSendAsync(ClientHandler handler, Message message)
    {
        try
        {
            await handler.SendAsync(message);
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or
                                          OperationCanceledException or ObjectDisposedException)
        {
            // ClientHandler tương ứng sẽ tự dọn registry khi receive loop kết thúc.
        }
    }

    private async Task SafeSendMemberListAsync(ClientHandler handler, Guid groupId)
    {
        try
        {
            await SendMemberListAsync(handler, groupId);
        }
        catch (Exception exception) when (exception is IOException or System.Net.Sockets.SocketException or
                                          OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private List<ClientHandler> GetOnlineHandlers(IEnumerable<string> usernames) => usernames
        .Select(username => _onlineUsers.TryGetValue(username, out var handler) ? handler : null)
        .Where(handler => handler is not null)
        .Cast<ClientHandler>()
        .Distinct()
        .ToList();

    private static bool TryGetUsername(ClientHandler handler, out string username)
    {
        username = handler.Username ?? string.Empty;
        return username.Length > 0;
    }
}
