namespace ChatProtocol;

public sealed class Message
{
    public MessageType Type { get; set; }
    public Guid? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? Sender { get; set; }
    public string? TargetUsername { get; set; }
    public string? Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<GroupInfo>? Groups { get; set; }
    public List<MemberInfo>? Members { get; set; }
}

public sealed class GroupInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class MemberInfo
{
    public string Username { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
}
