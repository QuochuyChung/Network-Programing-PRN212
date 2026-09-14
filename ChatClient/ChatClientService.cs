using System.IO;
using System.Net.Sockets;
using ChatProtocol;
using ProtocolMessage = ChatProtocol.Message;

namespace ChatClient;

public sealed class ChatClientService : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private Task? _receiveTask;

    public string Username { get; private set; } = string.Empty;
    public event Action<ProtocolMessage>? MessageReceived;
    public event Action<string>? Disconnected;

    public async Task LoginAsync(string host, int port, string username)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, _cancellation.Token);
        _stream = _client.GetStream();

        await SendAsync(new ProtocolMessage
        {
            Type = MessageType.Login,
            Sender = username.Trim()
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await FrameReader.ReadAsync(_stream, timeout.Token);
        if (response?.Type != MessageType.LoginOk)
        {
            var error = response?.Content ?? "Server đã đóng kết nối.";
            throw new InvalidOperationException(error);
        }

        Username = response.Sender ?? username.Trim();
        _receiveTask = ReceiveLoopAsync();
    }

    public Task SendAsync(ProtocolMessage message)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Chưa kết nối tới server.");
        }

        return FrameWriter.WriteAsync(_stream, message, _writeLock, _cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _client?.Dispose();

        if (_receiveTask is not null && Task.CurrentId != _receiveTask.Id)
        {
            try
            {
                await _receiveTask;
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Expected while closing the application.
            }
        }

        _cancellation.Dispose();
        _writeLock.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested && _stream is not null)
            {
                var message = await FrameReader.ReadAsync(_stream, _cancellation.Token);
                if (message is null)
                {
                    Disconnected?.Invoke("Server đã đóng kết nối.");
                    return;
                }

                MessageReceived?.Invoke(message);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            if (!_cancellation.IsCancellationRequested)
            {
                Disconnected?.Invoke($"Mất kết nối tới server: {exception.Message}");
            }
        }
    }
}
