using System.Text.Json;
using ChatProtocol;
using ChatProtocol.Dtos;
using ChatServer.Data;
using ChatServer.Data.Models;

namespace ChatServer.Services;

public class GroupService
{
    private readonly ClientHandler _client;

    public GroupService(ClientHandler client)
    {
        _client = client;
    }

    public void HandleGroupList(Message message)
    {
        try
        {
            using var db = new ChatDbContext();
            var user = db.Users.FirstOrDefault(u => u.Username.ToLower() == _client.Username.ToLower());
            if (user == null) return;

            var groups = db.GroupMembers
                .Where(gm => gm.UserId == user.Id)
                .Select(gm => gm.Group)
                .ToList();

            var dtos = new List<GroupDto>();
            foreach (var g in groups)
            {
                var lastMsg = db.Messages
                    .Where(m => m.GroupId == g.Id)
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefault();

                int count = db.GroupMembers.Count(gm => gm.GroupId == g.Id);

                dtos.Add(new GroupDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    LastMessage = lastMsg?.Content,
                    LastActivity = lastMsg?.SentAt ?? g.CreatedAt,
                    MemberCount = count
                });
            }

            dtos = dtos.OrderByDescending(d => d.LastActivity ?? DateTime.MinValue).ToList();

            _client.Send(new Message
            {
                Type = MessageType.GROUP_LIST,
                Sender = "server",
                Content = JsonSerializer.Serialize(dtos),
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GROUP_LIST ERROR] {_client.Username}: {ex.Message}");
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = $"Failed to load groups: {ex.Message}"
            });
        }
    }

    public void HandleCreateGroup(Message message)
    {
        string groupName = message.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(groupName))
        {
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = "Group name cannot be empty."
            });
            return;
        }

        try
        {
            using var db = new ChatDbContext();
            var creator = db.Users.FirstOrDefault(u => u.Username.ToLower() == _client.Username.ToLower());
            if (creator == null)
            {
                _client.Send(new Message
                {
                    Type = MessageType.ERROR,
                    Sender = "server",
                    Content = "User not found in database."
                });
                return;
            }

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

            var addedUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { creator.Username };
            if (message.Users != null)
            {
                foreach (var memberUsername in message.Users)
                {
                    string trimmed = memberUsername.Trim();
                    if (string.IsNullOrEmpty(trimmed) || addedUsernames.Contains(trimmed))
                        continue;

                    var memberUser = db.Users.FirstOrDefault(u => u.Username.ToLower() == trimmed.ToLower());
                    if (memberUser != null)
                    {
                        db.GroupMembers.Add(new GroupMember
                        {
                            GroupId = group.Id,
                            UserId = memberUser.Id,
                            JoinedAt = DateTime.UtcNow
                        });
                        addedUsernames.Add(memberUser.Username);
                    }
                }
            }

            var sysMsg = new MessageRecord
            {
                Id = Guid.NewGuid(),
                GroupId = group.Id,
                Sender = "system",
                Content = $"Group '{group.Name}' created by {_client.Username}.",
                SentAt = DateTime.UtcNow
            };
            db.Messages.Add(sysMsg);
            db.SaveChanges();

            var groupDto = new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                LastMessage = sysMsg.Content,
                LastActivity = sysMsg.SentAt,
                MemberCount = addedUsernames.Count
            };

            _client.Send(new Message
            {
                Type = MessageType.CREATE_GROUP,
                GroupId = group.Id,
                Sender = "server",
                Content = JsonSerializer.Serialize(groupDto),
                Timestamp = DateTime.UtcNow
            });

            BroadcastGroupListUpdate(db, addedUsernames);

            Console.WriteLine($"[GROUP CREATED] '{group.Name}' ({group.Id}) by {_client.Username} with {addedUsernames.Count} members.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CREATE_GROUP ERROR] {_client.Username}: {ex.Message}");
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = $"Failed to create group: {ex.Message}"
            });
        }
    }

    public void HandleAddMember(Message message)
    {
        string targetUsername = message.Content?.Trim() ?? string.Empty;
        Guid groupId = message.GroupId;

        if (groupId == Guid.Empty || string.IsNullOrEmpty(targetUsername))
        {
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = "Invalid group ID or username."
            });
            return;
        }

        try
        {
            using var db = new ChatDbContext();
            var group = db.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null)
            {
                _client.Send(new Message
                {
                    Type = MessageType.ERROR,
                    Sender = "server",
                    Content = "Group does not exist."
                });
                return;
            }

            var targetUser = db.Users.FirstOrDefault(u => u.Username.ToLower() == targetUsername.ToLower());
            if (targetUser == null)
            {
                _client.Send(new Message
                {
                    Type = MessageType.ERROR,
                    Sender = "server",
                    Content = $"User '{targetUsername}' does not exist."
                });
                return;
            }

            bool isMember = db.GroupMembers.Any(gm => gm.GroupId == groupId && gm.UserId == targetUser.Id);
            if (isMember)
            {
                _client.Send(new Message
                {
                    Type = MessageType.ERROR,
                    Sender = "server",
                    Content = $"User '{targetUser.Username}' is already a member of this group."
                });
                return;
            }

            db.GroupMembers.Add(new GroupMember
            {
                GroupId = groupId,
                UserId = targetUser.Id,
                JoinedAt = DateTime.UtcNow
            });

            var sysMsg = new MessageRecord
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Sender = "system",
                Content = $"{_client.Username} added {targetUser.Username} to the group.",
                SentAt = DateTime.UtcNow
            };
            db.Messages.Add(sysMsg);
            db.SaveChanges();

            var members = db.GroupMembers
                .Where(gm => gm.GroupId == groupId)
                .Select(gm => gm.User.Username)
                .ToList();

            var chatMsg = new Message
            {
                Type = MessageType.MESSAGE,
                GroupId = groupId,
                Sender = "system",
                Content = sysMsg.Content,
                Timestamp = sysMsg.SentAt
            };

            List<ClientHandler> onlineTargets = new();
            lock (ClientHandler.Lock)
            {
                foreach (var m in members)
                {
                    if (ClientHandler.OnlineUsers.TryGetValue(m, out var handler))
                    {
                        onlineTargets.Add(handler);
                    }
                }
            }

            foreach (var h in onlineTargets)
            {
                h.Send(chatMsg);
            }

            BroadcastMemberList(db, groupId);
            BroadcastGroupListUpdate(db, members);

            Console.WriteLine($"[MEMBER ADDED] {targetUser.Username} added to '{group.Name}' by {_client.Username}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ADD_MEMBER ERROR] {_client.Username}: {ex.Message}");
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = $"Failed to add member: {ex.Message}"
            });
        }
    }

    public void HandleOpenGroup(Message message)
    {
        Guid groupId = message.GroupId;
        if (groupId == Guid.Empty) return;

        try
        {
            using var db = new ChatDbContext();

            var members = db.GroupMembers
                .Where(gm => gm.GroupId == groupId)
                .OrderBy(gm => gm.JoinedAt)
                .Select(gm => new { gm.User.Username, gm.JoinedAt })
                .ToList();

            var memberDtos = new List<GroupMemberDto>();
            lock (ClientHandler.Lock)
            {
                foreach (var m in members)
                {
                    memberDtos.Add(new GroupMemberDto
                    {
                        Username = m.Username,
                        IsOnline = ClientHandler.OnlineUsers.ContainsKey(m.Username),
                        JoinedAt = m.JoinedAt
                    });
                }
            }

            _client.Send(new Message
            {
                Type = MessageType.MEMBER_LIST,
                GroupId = groupId,
                Sender = "server",
                Content = JsonSerializer.Serialize(memberDtos),
                Timestamp = DateTime.UtcNow
            });

            var history = db.Messages
                .Where(m => m.GroupId == groupId)
                .OrderBy(m => m.SentAt)
                .Take(100)
                .Select(m => new MessageHistoryItemDto
                {
                    Id = m.Id,
                    GroupId = m.GroupId,
                    Sender = m.Sender,
                    Content = m.Content,
                    SentAt = m.SentAt
                })
                .ToList();

            _client.Send(new Message
            {
                Type = MessageType.MESSAGE_HISTORY,
                GroupId = groupId,
                Sender = "server",
                Content = JsonSerializer.Serialize(history),
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OPEN_GROUP ERROR] {_client.Username}: {ex.Message}");
            _client.Send(new Message
            {
                Type = MessageType.ERROR,
                Sender = "server",
                Content = $"Failed to open group: {ex.Message}"
            });
        }
    }

    public void HandleUserList(Message message)
    {
        try
        {
            using var db = new ChatDbContext();
            var users = db.Users
                .Select(u => u.Username)
                .OrderBy(u => u)
                .ToList();

            _client.Send(new Message
            {
                Type = MessageType.USER_LIST,
                Sender = "server",
                Users = users,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[USER_LIST ERROR] {_client.Username}: {ex.Message}");
        }
    }

    private void BroadcastMemberList(ChatDbContext db, Guid groupId)
    {
        var members = db.GroupMembers
            .Where(gm => gm.GroupId == groupId)
            .OrderBy(gm => gm.JoinedAt)
            .Select(gm => new { gm.User.Username, gm.JoinedAt })
            .ToList();

        var memberDtos = new List<GroupMemberDto>();
        List<ClientHandler> onlineTargets = new();

        lock (ClientHandler.Lock)
        {
            foreach (var m in members)
            {
                bool isOnline = ClientHandler.OnlineUsers.ContainsKey(m.Username);
                memberDtos.Add(new GroupMemberDto
                {
                    Username = m.Username,
                    IsOnline = isOnline,
                    JoinedAt = m.JoinedAt
                });

                if (ClientHandler.OnlineUsers.TryGetValue(m.Username, out var handler))
                {
                    onlineTargets.Add(handler);
                }
            }
        }

        var updateMsg = new Message
        {
            Type = MessageType.MEMBER_LIST,
            GroupId = groupId,
            Sender = "server",
            Content = JsonSerializer.Serialize(memberDtos),
            Timestamp = DateTime.UtcNow
        };

        foreach (var handler in onlineTargets)
        {
            handler.Send(updateMsg);
        }
    }

    private void BroadcastGroupListUpdate(ChatDbContext db, IEnumerable<string> usernames)
    {
        foreach (var username in usernames)
        {
            ClientHandler? targetHandler = null;
            lock (ClientHandler.Lock)
            {
                ClientHandler.OnlineUsers.TryGetValue(username, out targetHandler);
            }

            if (targetHandler != null)
            {
                var targetService = new GroupService(targetHandler);
                targetService.HandleGroupList(new Message { Type = MessageType.GROUP_LIST });
            }
        }
    }
}
