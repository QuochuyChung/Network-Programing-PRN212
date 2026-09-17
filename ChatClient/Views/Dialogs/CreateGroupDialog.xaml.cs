using System.Windows;
using ChatClient.ViewModels;

namespace ChatClient.Views.Dialogs;

public partial class CreateGroupDialog : Window
{
    public CreateGroupViewModel ViewModel { get; }

    public CreateGroupDialog()
    {
        InitializeComponent();
        ViewModel = new CreateGroupViewModel();
        DataContext = ViewModel;

        ViewModel.CloseRequested += () => Close();
    }
}
