namespace ChatProtocol.Dtos;

public class GroupMemberDto
{
    public string Username { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime JoinedAt { get; set; }
}
