namespace ChatProtocol.Dtos;

public class GroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LastMessage { get; set; }
    public DateTime? LastActivity { get; set; }
    public int MemberCount { get; set; }
}
