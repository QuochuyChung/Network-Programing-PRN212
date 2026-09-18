using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ChatClient.Services;
using ChatClient.ViewModels;
using ChatClient.Views;
using ChatClient.Views.Dialogs;
using ChatProtocol.Dtos;

namespace ChatClient;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;

        ViewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        ViewModel.LogoutRequested += OnLogoutRequested;
        ViewModel.ForceLogoutRequested += OnForceLogoutRequested;
        ViewModel.OpenCreateGroupDialogRequested += OnOpenCreateGroupDialogRequested;
        ViewModel.OpenAddMemberDialogRequested += OnOpenAddMemberDialogRequested;
        ViewModel.RequestOpenImageViewer += OnRequestOpenImageViewer;
    }

    private void OnRequestOpenImageViewer(FileAttachmentDto attachment, ImageSource? thumbnailSource)
    {
        Dispatcher.Invoke(() =>
        {
            var viewer = new ImageViewerWindow(attachment, thumbnailSource)
            {
                Owner = this
            };
            viewer.ShowDialog();
        });
    }

    private void OnScrollToBottomRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MessagesListBox.Items.Count > 0)
            {
                var lastItem = MessagesListBox.Items[MessagesListBox.Items.Count - 1];
                MessagesListBox.ScrollIntoView(lastItem);
            }
        });
    }

    private void OnLogoutRequested()
    {
        Dispatcher.Invoke(() =>
        {
            var loginWindow = new LoginWindow();
            loginWindow.Show();
            Close();
        });
    }

    private void OnForceLogoutRequested(string reason)
    {
        Dispatcher.Invoke(() =>
        {
            var loginWindow = new LoginWindow(reason);
            loginWindow.Show();
            Close();
        });
    }

    private void OnOpenCreateGroupDialogRequested()
    {
        var dialog = new CreateGroupDialog
        {
            Owner = this
        };

        dialog.ViewModel.GroupCreationConfirmed += (name, members) =>
        {
            ChatClientService.Instance.CreateGroup(name, members);
        };

        dialog.ShowDialog();
    }

    private void OnOpenAddMemberDialogRequested(Guid groupId, string groupName)
    {
        var dialog = new AddMemberDialog(groupId, groupName)
        {
            Owner = this
        };

        dialog.ViewModel.MemberAddConfirmed += (gid, username) =>
        {
            ChatClientService.Instance.AddMember(gid, username);
        };

        dialog.ShowDialog();
    }

    private void OnEmojiButtonClick(object sender, RoutedEventArgs e)
    {
        EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
    }

    private void OnEmojiSelected(string emoji)
    {
        if (string.IsNullOrEmpty(TxtMessageInput.Text))
        {
            TxtMessageInput.Text = emoji;
            TxtMessageInput.CaretPosition = TxtMessageInput.Document.ContentEnd;
        }
        else if (!TxtMessageInput.Selection.IsEmpty)
        {
            new TextRange(TxtMessageInput.Selection.Start, TxtMessageInput.Selection.End).Text = emoji;
            TxtMessageInput.CaretPosition = TxtMessageInput.Selection.End;
        }
        else
        {
            var pointer = TxtMessageInput.CaretPosition?.GetInsertionPosition(LogicalDirection.Forward)
                          ?? TxtMessageInput.Document.ContentEnd;
            pointer.InsertTextInRun(emoji);
            TxtMessageInput.CaretPosition = pointer.GetPositionAtOffset(emoji.Length, LogicalDirection.Forward) 
                                            ?? TxtMessageInput.Document.ContentEnd;
        }
        TxtMessageInput.Focus();
    }

    private void OnMessageInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (ViewModel.SendMessageCommand.CanExecute(null))
            {
                ViewModel.SendMessageCommand.Execute(null);
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ViewModel.UnregisterEvents();
        if (ChatClientService.Instance.IsConnected)
        {
            ChatClientService.Instance.Disconnect();
        }
    }
}
