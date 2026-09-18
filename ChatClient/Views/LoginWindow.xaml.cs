using System.Windows;
using ChatClient.Services;
using ChatClient.ViewModels;

namespace ChatClient.Views;

public partial class LoginWindow : Window
{
    public LoginViewModel ViewModel { get; }

    public LoginWindow() : this(null)
    {
    }

    public LoginWindow(string? initialError)
    {
        InitializeComponent();
        ViewModel = new LoginViewModel(initialError);
        DataContext = ViewModel;

        ViewModel.LoginSuccess += OnLoginSuccess;
        TxtUsername.Focus();
    }

    private void OnLoginSuccess(string username)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                ChatClientService.Instance.StartMessageLoop();
                var nextWindow = new MainWindow();
                nextWindow.Title = $"ChatApp • Logged in as: @{username}";
                Application.Current.MainWindow = nextWindow;
                nextWindow.Show();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch Chat Window: {ex.Message}\n\n{ex.StackTrace}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }
}
