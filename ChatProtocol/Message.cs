using ChatProtocol;

public class Message
{
    public MessageType Type {get; set;}
    public Guid GroupId {get; set;}
    public string Sender {get; set;} = string.Empty;
    public string Content {get; set;} = string.Empty;
    public DateTime Timestamp {get; set;}
}