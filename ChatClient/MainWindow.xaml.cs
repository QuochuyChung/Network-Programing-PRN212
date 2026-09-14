using System.Collections.ObjectModel;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using ChatProtocol;

namespace ChatClient;

public partial class MainWindow : Window
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<string> _onlineUsers = [];

    public MainWindow()
    {
        InitializeComponent();
        OnlineUsersList.ItemsSource = _onlineUsers;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var dialog = new TextInputDialog { Owner = this };
        if (dialog.ShowDialog() != true) { Close(); return; }

        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync("127.0.0.1", 5000);
            _stream = _tcpClient.GetStream();

            // Send LOGIN
            FrameWriter.WriteMessage(_stream, new Message
            {
                Type = MessageType.LOGIN,
                Sender = dialog.Username,
                Timestamp = DateTime.UtcNow
            });

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not connect to server: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void ReadLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                Message? message = FrameReader.ReadMessage(_stream!);
                if (message == null) break;

                Dispatcher.BeginInvoke(() => HandleMessage(message));
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        catch { }

        Dispatcher.BeginInvoke(() =>
        {
            Title = "Chat (disconnected)";
        });
    }

    private void HandleMessage(Message message)
    {
        switch (message.Type)
        {
            case MessageType.LOGIN_OK:
                Title = $"Chat ({message.Sender})";
                break;

            case MessageType.ONLINE_LIST:
                _onlineUsers.Clear();
                if (message.Users != null)
                    foreach (var u in message.Users)
                        _onlineUsers.Add(u);
                break;

            case MessageType.USER_ONLINE:
                if (!string.IsNullOrEmpty(message.Content) && !_onlineUsers.Contains(message.Content))
                    _onlineUsers.Add(message.Content);
                break;

            case MessageType.USER_OFFLINE:
                if (!string.IsNullOrEmpty(message.Content))
                    _onlineUsers.Remove(message.Content);
                break;
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
        _stream?.Close();
        _tcpClient?.Close();
    }
}
