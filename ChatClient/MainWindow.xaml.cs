using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ChatClient.Services;
using ChatClient.Views;
using ChatProtocol;

namespace ChatClient;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<string> _onlineUsers = [];
    private bool _isForceLoggedOut = false;

    public MainWindow()
    {
        InitializeComponent();
        OnlineUsersList.ItemsSource = _onlineUsers;

        ChatClientService.Instance.OnForceLogout += HandleForceLogout;
        ChatClientService.Instance.OnMessageReceived += HandleMessage;

        var username = ChatClientService.Instance.CurrentUser;
        if (username != null)
        {
            Title = $"Chat ({username})";
            MyUsernameText.Text = username;
            MyAvatarText.Text = username[0].ToString().ToUpper();
        }
    }

    private void HandleForceLogout(string reason)
    {
        _isForceLoggedOut = true;
        Dispatcher.Invoke(() =>
        {
            var loginWindow = new LoginWindow(reason);
            loginWindow.Show();
            this.Close();
        });
    }

    private void HandleMessage(Message message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            switch (message.Type)
            {
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
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ChatClientService.Instance.OnForceLogout -= HandleForceLogout;
        ChatClientService.Instance.OnMessageReceived -= HandleMessage;
        if (!_isForceLoggedOut)
        {
            ChatClientService.Instance.Disconnect();
        }
    }

    private void UserButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string username)
        {
            Title = $"Chat — selected: {username}";
        }
    }
}
