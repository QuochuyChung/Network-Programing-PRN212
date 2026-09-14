using System.Collections.Concurrent;
using ChatProtocol;
using ChatServer.Data;
using ChatServer.Data.Models;
using Microsoft.EntityFrameworkCore;
using ProtocolMessage = ChatProtocol.Message;

namespace ChatServer;

public sealed class GroupManager
{
    private readonly ConcurrentDictionary<string, ClientHandler> _onlineUsers =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<string?> LoginAsync(ClientHandler handler, string? rawUsername)
    {
        var username = rawUsername?.Trim();
        if (string.IsNullOrWhiteSpace(username) || username.Length > 50)
        {
            await handler.SendErrorAsync("Username phải có từ 1 đến 50 ký tự.");
            return null;
        }

        if (!_onlineUsers.TryAdd(username, handler))
        {
            await handler.SendErrorAsync("Username này đang đăng nhập trên một thiết bị khác.");
            return null;
        }

        try
        {
            await using var db = new ChatDbContext();
            var user = await db.Users.SingleOrDefaultAsync(item => item.Username == username);
            if (user is null)
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Username = username,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await handler.SendAsync(new ProtocolMessage
            {
                Type = MessageType.LoginOk,
                Sender = username,
                Content = "Đăng nhập thành công."
            });
            return username;
        }
        catch (Exception exception)
        {
            _onlineUsers.TryRemove(new KeyValuePair<string, ClientHandler>(username, handler));
            Console.Error.WriteLine($"Login failed for {username}: {exception}");
            await handler.SendErrorAsync("Không thể đăng nhập do lỗi cơ sở dữ liệu.");
            return null;
        }
    }

    public async Task SendGroupListAsync(ClientHandler handler)
    {
        if (!TryGetUsername(handler, out var username))
        {
            return;
        }

        await using var db = new ChatDbContext();
        var groups = await db.GroupMembers
            .Where(member => member.User.Username == username)
            .OrderBy(member => member.Group.Name)
            .Select(member => new GroupInfo { Id = member.GroupId, Name = member.Group.Name })
            .ToListAsync();

        await handler.SendAsync(new ProtocolMessage
        {
            Type = MessageType.GroupList,
            Groups = groups
        });
    }

    public async Task CreateGroupAsync(ClientHandler handler, string? rawGroupName)
    {
        if (!TryGetUsername(handler, out var username))
        {
            return;
        }

        var groupName = rawGroupName?.Trim();
        if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > 100)
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

        await handler.SendAsync(new ProtocolMessage
        {
            Type = MessageType.CreateGroup,
            GroupId = group.Id,
            GroupName = group.Name,
            Content = $"Đã tạo nhóm “{group.Name}”."
        });
        await SendGroupListAsync(handler);
        await SendMemberListAsync(handler, group.Id);
    }

    public async Task AddMemberAsync(ClientHandler handler, Guid? groupId, string? rawTargetUsername)
    {
        if (!TryGetUsername(handler, out var username))
        {
            return;
        }

        if (groupId is null || groupId == Guid.Empty)
        {
            await handler.SendErrorAsync("Bạn chưa chọn nhóm.");
            return;
        }

        var targetUsername = rawTargetUsername?.Trim();
        if (string.IsNullOrWhiteSpace(targetUsername) || targetUsername.Length > 50)
        {
            await handler.SendErrorAsync("Username cần thêm phải có từ 1 đến 50 ký tự.");
            return;
        }

        await using var db = new ChatDbContext();
        var requesterIsMember = await db.GroupMembers.AnyAsync(member =>
            member.GroupId == groupId && member.User.Username == username);
        if (!requesterIsMember)
        {
            await handler.SendErrorAsync("Bạn không phải thành viên của nhóm này.");
            return;
        }

        var targetUser = await db.Users.SingleOrDefaultAsync(user => user.Username == targetUsername);
        if (targetUser is null)
        {
            await handler.SendErrorAsync($"Không tìm thấy người dùng “{targetUsername}”. Người này cần đăng nhập ít nhất một lần.");
            return;
        }

        var alreadyMember = await db.GroupMembers.AnyAsync(member =>
            member.GroupId == groupId && member.UserId == targetUser.Id);
        if (alreadyMember)
        {
            await handler.SendErrorAsync($"{targetUser.Username} đã ở trong nhóm.");
            return;
        }

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = groupId.Value,
            UserId = targetUser.Id,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var groupName = await db.Groups
            .Where(group => group.Id == groupId)
            .Select(group => group.Name)
            .SingleAsync();

        await handler.SendAsync(new ProtocolMessage
        {
            Type = MessageType.AddMember,
            GroupId = groupId,
            GroupName = groupName,
            TargetUsername = targetUser.Username,
            Content = $"Đã thêm {targetUser.Username} vào nhóm."
        });

        if (_onlineUsers.TryGetValue(targetUser.Username, out var targetHandler))
        {
            await SendGroupListAsync(targetHandler);
        }

        await BroadcastMemberListAsync(groupId.Value);
    }

    public async Task SendMemberListAsync(ClientHandler handler, Guid? groupId)
    {
        if (!TryGetUsername(handler, out var username) || groupId is null)
        {
            await handler.SendErrorAsync("Nhóm không hợp lệ.");
            return;
        }

        await using var db = new ChatDbContext();
        var isMember = await db.GroupMembers.AnyAsync(member =>
            member.GroupId == groupId && member.User.Username == username);
        if (!isMember)
        {
            await handler.SendErrorAsync("Bạn không phải thành viên của nhóm này.");
            return;
        }

        var memberNames = await db.GroupMembers
            .Where(member => member.GroupId == groupId)
            .OrderBy(member => member.User.Username)
            .Select(member => member.User.Username)
            .ToListAsync();

        await handler.SendAsync(new ProtocolMessage
        {
            Type = MessageType.MemberList,
            GroupId = groupId,
            Members = memberNames.Select(name => new MemberInfo
            {
                Username = name,
                IsOnline = _onlineUsers.ContainsKey(name)
            }).ToList()
        });
    }

    public void Disconnect(ClientHandler handler)
    {
        if (handler.Username is not null)
        {
            _onlineUsers.TryRemove(new KeyValuePair<string, ClientHandler>(handler.Username, handler));
        }
    }

    private async Task BroadcastMemberListAsync(Guid groupId)
    {
        await using var db = new ChatDbContext();
        var usernames = await db.GroupMembers
            .Where(member => member.GroupId == groupId)
            .Select(member => member.User.Username)
            .ToListAsync();

        var handlers = usernames
            .Select(username => _onlineUsers.TryGetValue(username, out var handler) ? handler : null)
            .Where(handler => handler is not null)
            .Cast<ClientHandler>();
        await Task.WhenAll(handlers.Select(handler => SendMemberListAsync(handler, groupId)));
    }

    private static bool TryGetUsername(ClientHandler handler, out string username)
    {
        username = handler.Username ?? string.Empty;
        return username.Length > 0;
    }
}
