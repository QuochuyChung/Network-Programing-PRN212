using System.Windows;
using System.Windows.Input;

namespace ChatClient;

public partial class AddMemberWindow : Window
{
    public string Username { get; private set; } = string.Empty;

    public AddMemberWindow(string groupName)
    {
        InitializeComponent();
        DescriptionTextBlock.Text = $"Thêm một người vào nhóm “{groupName}”.";
        Loaded += (_, _) => UsernameTextBox.Focus();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        Username = UsernameTextBox.Text.Trim();
        if (Username.Length == 0)
        {
            ErrorTextBlock.Text = "Vui lòng nhập username.";
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddButton_Click(sender, new RoutedEventArgs());
        }
    }
}
