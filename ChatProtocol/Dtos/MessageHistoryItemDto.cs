namespace ChatProtocol.Dtos;

public class MessageHistoryItemDto
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
