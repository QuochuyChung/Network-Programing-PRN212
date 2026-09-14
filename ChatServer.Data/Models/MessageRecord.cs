namespace ChatServer.Data.Models;

// Ten la "MessageRecord" (khong phai "Message") de tranh trung voi
// class Message ben ChatProtocol dung cho giao thuc mang.
public class MessageRecord
{
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public string Sender { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime SentAt { get; set; }
}
