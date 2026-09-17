using System.Windows;
using System.Windows.Controls;
using ChatClient.Services;

namespace ChatClient.Views;

public partial class LoginWindow : Window
{
    private readonly ClientConfigService _config;

    public LoginWindow()
    {
        InitializeComponent();
        _config = ClientConfigService.Load();
        TxtUsername.Focus();
    }

    private void OnUsernameTextChanged(object sender, TextChangedEventArgs e)
    {
        UsernamePlaceholder.Visibility = string.IsNullOrEmpty(TxtUsername.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        HideError();
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        string username = TxtUsername.Text.Trim();

        if (string.IsNullOrEmpty(username))
        {
            ShowError("Please enter your username / nickname to join.");
            TxtUsername.Focus();
            return;
        }

        // Start connection and login
        SetLoading(true);
        HideError();

        var (success, message) = await ChatClientService.Instance.ConnectAndLoginAsync(
            _config.Host, 
            _config.Port, 
            username);

        SetLoading(false);

        if (success)
        {
            // Sau khi đăng nhập, mở màn hình quản lý group trong thư mục Views.
            var nextWindow = new GroupListWindow();
            nextWindow.Title = $"ChatApp • Logged in as: @{ChatClientService.Instance.CurrentUser}";
            nextWindow.Show();
            this.Close();
        }
        else
        {
            ShowError(message);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBadge.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        if (ErrorBadge.Visibility != Visibility.Collapsed)
        {
            ErrorBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void SetLoading(bool isLoading)
    {
        BtnLogin.IsEnabled = !isLoading;
        BtnLogin.Content = isLoading ? "Connecting..." : "Join Chat";
        TxtUsername.IsEnabled = !isLoading;
    }
}
