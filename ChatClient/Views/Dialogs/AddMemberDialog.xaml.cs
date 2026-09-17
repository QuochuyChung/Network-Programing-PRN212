using System.Windows;
using ChatClient.ViewModels;

namespace ChatClient.Views.Dialogs;

public partial class AddMemberDialog : Window
{
    public AddMemberViewModel ViewModel { get; }

    public AddMemberDialog(Guid groupId, string groupName)
    {
        InitializeComponent();
        ViewModel = new AddMemberViewModel(groupId, groupName);
        DataContext = ViewModel;

        ViewModel.CloseRequested += () => Close();
    }
}
