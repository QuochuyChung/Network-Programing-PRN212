using System.IO;
using System.Net.Sockets;
using ChatProtocol;

namespace ChatServer;

public sealed class ClientHandler
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly GroupManager _groupManager;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();

    public ClientHandler(TcpClient client, GroupManager groupManager)
    {
        _client = client;
        _stream = client.GetStream();
        _groupManager = groupManager;
    }

    public string? Username { get; internal set; }

    public async Task RunAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                Message? message = await FrameReader.ReadAsync(_stream, _cancellation.Token);
                if (message is null)
                {
                    break;
                }

                try
                {
                    await HandleMessageAsync(message);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"Lỗi xử lý {message.Type} của {Username ?? "client chưa đăng nhập"}: {exception}");
                    await SendErrorAsync("Server không thể xử lý yêu cầu. Vui lòng thử lại.");
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            Console.WriteLine($"Client {Username ?? "chưa đăng nhập"} mất kết nối: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lỗi xử lý client {Username ?? "chưa đăng nhập"}: {exception}");
            try
            {
                await SendErrorAsync("Server không thể xử lý yêu cầu.");
            }
            catch
            {
            }
        }
        finally
        {
            await DisconnectAsync();
        }
    }

    public Task SendAsync(Message message) =>
        FrameWriter.WriteAsync(_stream, message, _writeLock, _cancellation.Token);

    public Task SendErrorAsync(string content) => SendAsync(new Message
    {
        Type = MessageType.ERROR,
        Sender = "server",
        Content = content,
        Timestamp = DateTime.UtcNow
    });

    private async Task HandleMessageAsync(Message message)
    {
        if (message.Type == MessageType.LOGIN)
        {
            if (Username is not null)
            {
                await SendErrorAsync("Client này đã đăng nhập.");
                return;
            }

            await _groupManager.LoginAsync(this, message.Sender);
            return;
        }

        if (Username is null)
        {
            await SendErrorAsync("Bạn cần đăng nhập trước khi thực hiện yêu cầu.");
            return;
        }

        switch (message.Type)
        {
            case MessageType.GROUP_LIST:
                await _groupManager.SendGroupListAsync(this);
                break;
            case MessageType.CREATE_GROUP:
                await _groupManager.CreateGroupAsync(this, message.GroupName);
                break;
            case MessageType.ADD_MEMBER:
                await _groupManager.AddMemberAsync(this, message.GroupId, message.TargetUsername);
                break;
            case MessageType.OPEN_GROUP:
                await _groupManager.SendMemberListAsync(this, message.GroupId);
                break;
            case MessageType.MESSAGE:
                await _groupManager.SendChatMessageAsync(this, message);
                break;
            default:
                await SendErrorAsync($"Message type {message.Type} không được client gửi lên server.");
                break;
        }
    }

    private async Task DisconnectAsync()
    {
        await _cancellation.CancelAsync();
        try
        {
            await _groupManager.DisconnectAsync(this);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Không thể broadcast trạng thái offline: {exception.Message}");
        }
        _client.Dispose();
        _cancellation.Dispose();
        _writeLock.Dispose();
        Console.WriteLine($"{Username ?? "Client"} đã ngắt kết nối.");
    }
}
