using System.Net.Sockets;
using ChatProtocol;
using ProtocolMessage = ChatProtocol.Message;

namespace ChatServer;

public sealed class ClientHandler
{
    private readonly TcpClient _client;
    private readonly GroupManager _groupManager;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string? Username { get; private set; }

    public ClientHandler(TcpClient client, GroupManager groupManager)
    {
        _client = client;
        _groupManager = groupManager;
        _stream = client.GetStream();
    }

    public async Task RunAsync()
    {
        try
        {
            while (true)
            {
                var message = await FrameReader.ReadAsync(_stream);
                if (message is null)
                {
                    break;
                }

                if (Username is null && message.Type != MessageType.Login)
                {
                    await SendErrorAsync("Bạn cần đăng nhập trước khi sử dụng tính năng này.");
                    continue;
                }

                switch (message.Type)
                {
                    case MessageType.Login:
                        await HandleLoginAsync(message);
                        break;
                    case MessageType.GroupList:
                        await _groupManager.SendGroupListAsync(this);
                        break;
                    case MessageType.CreateGroup:
                        await _groupManager.CreateGroupAsync(this, message.GroupName);
                        break;
                    case MessageType.AddMember:
                        await _groupManager.AddMemberAsync(this, message.GroupId, message.TargetUsername);
                        break;
                    case MessageType.OpenGroup:
                        await _groupManager.SendMemberListAsync(this, message.GroupId);
                        break;
                    default:
                        await SendErrorAsync("Loại yêu cầu chưa được hỗ trợ.");
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            Console.WriteLine($"Client {Username ?? "unknown"} disconnected: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Client {Username ?? "unknown"} failed: {exception}");
        }
        finally
        {
            _groupManager.Disconnect(this);
            _client.Dispose();
            _writeLock.Dispose();
        }
    }

    public Task SendAsync(ProtocolMessage message) =>
        FrameWriter.WriteAsync(_stream, message, _writeLock);

    public Task SendErrorAsync(string error) => SendAsync(new ProtocolMessage
    {
        Type = MessageType.Error,
        Content = error
    });

    private async Task HandleLoginAsync(ProtocolMessage message)
    {
        if (Username is not null)
        {
            await SendErrorAsync("Kết nối này đã đăng nhập.");
            return;
        }

        var acceptedUsername = await _groupManager.LoginAsync(this, message.Sender);
        if (acceptedUsername is not null)
        {
            Username = acceptedUsername;
        }
    }
}
