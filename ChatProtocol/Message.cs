namespace ChatProtocol;

/// <summary>
/// Gói dữ liệu dùng chung cho mọi request/response giữa client và server.
/// Các trường không liên quan tới một loại message cụ thể sẽ giữ giá trị mặc định.
/// </summary>
public sealed class Message
{
    public MessageType Type { get; set; }
    public Guid GroupId { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string GroupName { get; set; } = string.Empty;
    public string TargetUsername { get; set; } = string.Empty;
    public List<GroupInfo> Groups { get; set; } = [];
    public List<MemberInfo> Members { get; set; } = [];
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
