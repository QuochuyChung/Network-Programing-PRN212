using System.Windows;
using System.Windows.Input;

namespace ChatClient;

public partial class TextInputDialog : Window
{
    public string Username { get; private set; } = "";

    public TextInputDialog()
    {
        InitializeComponent();
        UsernameTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            Username = UsernameTextBox.Text.Trim();
            DialogResult = true;
        }
    }

    private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            Username = UsernameTextBox.Text.Trim();
            DialogResult = true;
        }
    }
}
