using System.Configuration;
using System.Data;
using System.Windows;

namespace ChatClient;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"UI Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Application Error: {ex.Message}\n\n{ex.StackTrace}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }
}

