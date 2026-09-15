using System.Windows;
using ChatClient.Services;
using ChatClient.Views;

namespace ChatClient;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isForceLoggedOut = false;

    public MainWindow()
    {
        InitializeComponent();
        ChatClientService.Instance.OnForceLogout += HandleForceLogout;
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ChatClientService.Instance.OnForceLogout -= HandleForceLogout;
        if (!_isForceLoggedOut)
        {
            ChatClientService.Instance.Disconnect();
        }
    }
}