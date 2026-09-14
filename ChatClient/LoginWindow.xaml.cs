using System.Windows;
using System.Windows.Input;

namespace ChatClient;

public partial class LoginWindow : Window
{
    private ChatClientService? _service;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameTextBox.Focus();
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameTextBox.Text.Trim();
        if (username.Length == 0)
        {
            ShowError("Vui lòng nhập username.");
            return;
        }

        if (!int.TryParse(PortTextBox.Text, out var port) || port is < 1 or > 65535)
        {
            ShowError("Port phải là số từ 1 đến 65535.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Content = "Đang kết nối...";
        ErrorTextBlock.Text = string.Empty;

        try
        {
            _service = new ChatClientService();
            await _service.LoginAsync(HostTextBox.Text.Trim(), port, username);

            var groupListWindow = new GroupListWindow(_service);
            groupListWindow.Closed += (_, _) => Close();
            Hide();
            groupListWindow.Show();
        }
        catch (Exception exception)
        {
            if (_service is not null)
            {
                await _service.DisposeAsync();
                _service = null;
            }
            ShowError(exception.Message);
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Kết nối và đăng nhập";
        }
    }

    private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoginButton_Click(LoginButton, new RoutedEventArgs());
        }
    }

    private void ShowError(string error) => ErrorTextBlock.Text = error;
}
