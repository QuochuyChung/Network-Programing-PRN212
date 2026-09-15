using System.Windows;
using System.Windows.Input;

namespace ChatClient;

public partial class CreateGroupWindow : Window
{
    public string GroupName { get; private set; } = string.Empty;

    public CreateGroupWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => GroupNameTextBox.Focus();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        GroupName = GroupNameTextBox.Text.Trim();
        if (GroupName.Length == 0)
        {
            ErrorTextBlock.Text = "Vui lòng nhập tên nhóm.";
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void GroupNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateButton_Click(sender, new RoutedEventArgs());
        }
    }
}
