using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatProtocol;
using ProtocolMessage = ChatProtocol.Message;

namespace ChatClient;

public partial class GroupListWindow : Window
{
    private readonly ChatClientService _service;
    private Guid? _pendingSelection;

    public ObservableCollection<GroupInfo> Groups { get; } = [];
    public ObservableCollection<MemberInfo> Members { get; } = [];

    public GroupListWindow(ChatClientService service)
    {
        _service = service;
        InitializeComponent();
        DataContext = this;
        CurrentUserTextBlock.Text = $"Đang đăng nhập: {_service.Username}";

        _service.MessageReceived += Service_MessageReceived;
        _service.Disconnected += Service_Disconnected;
        Loaded += GroupListWindow_Loaded;
        Closed += GroupListWindow_Closed;
    }

    private async void GroupListWindow_Loaded(object sender, RoutedEventArgs e) =>
        await TrySendAsync(new ProtocolMessage { Type = MessageType.GroupList });

    private async void GroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Members.Clear();
        if (GroupsListBox.SelectedItem is not GroupInfo group)
        {
            SelectedGroupNameTextBlock.Text = "Chọn một nhóm";
            AddMemberButton.IsEnabled = false;
            return;
        }

        SelectedGroupNameTextBlock.Text = group.Name;
        AddMemberButton.IsEnabled = true;
        await TrySendAsync(new ProtocolMessage { Type = MessageType.OpenGroup, GroupId = group.Id });
    }

    private async void CreateGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateGroupWindow { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SetStatus("Đang tạo nhóm...", false);
            await TrySendAsync(new ProtocolMessage
            {
                Type = MessageType.CreateGroup,
                GroupName = dialog.GroupName
            });
        }
    }

    private async void AddMemberButton_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsListBox.SelectedItem is not GroupInfo group)
        {
            SetStatus("Vui lòng chọn nhóm trước.", true);
            return;
        }

        var dialog = new AddMemberWindow(group.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SetStatus($"Đang thêm {dialog.Username}...", false);
            await TrySendAsync(new ProtocolMessage
            {
                Type = MessageType.AddMember,
                GroupId = group.Id,
                TargetUsername = dialog.Username
            });
        }
    }

    private void Service_MessageReceived(ProtocolMessage message) =>
        Dispatcher.InvokeAsync(() => HandleMessage(message));

    private void Service_Disconnected(string reason) => Dispatcher.InvokeAsync(() =>
    {
        SetStatus(reason, true);
        AddMemberButton.IsEnabled = false;
    });

    private void HandleMessage(ProtocolMessage message)
    {
        switch (message.Type)
        {
            case MessageType.GroupList:
                var selectedId = _pendingSelection ?? (GroupsListBox.SelectedItem as GroupInfo)?.Id;
                Groups.Clear();
                foreach (var group in message.Groups ?? [])
                {
                    Groups.Add(group);
                }
                GroupCountTextBlock.Text = $"{Groups.Count} nhóm";
                GroupsListBox.SelectedItem = Groups.FirstOrDefault(group => group.Id == selectedId);
                _pendingSelection = null;
                break;

            case MessageType.CreateGroup:
                _pendingSelection = message.GroupId;
                SetStatus(message.Content ?? "Tạo nhóm thành công.", false);
                break;

            case MessageType.AddMember:
                SetStatus(message.Content ?? "Thêm thành viên thành công.", false);
                break;

            case MessageType.MemberList:
                if ((GroupsListBox.SelectedItem as GroupInfo)?.Id != message.GroupId)
                {
                    break;
                }
                Members.Clear();
                foreach (var member in message.Members ?? [])
                {
                    Members.Add(member);
                }
                break;

            case MessageType.Error:
                SetStatus(message.Content ?? "Yêu cầu không thành công.", true);
                break;
        }
    }

    private async Task TrySendAsync(ProtocolMessage message)
    {
        try
        {
            await _service.SendAsync(message);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, true);
        }
    }

    private void SetStatus(string text, bool isError)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(200, 52, 52)
            : Color.FromRgb(55, 119, 86));
    }

    private async void GroupListWindow_Closed(object? sender, EventArgs e)
    {
        _service.MessageReceived -= Service_MessageReceived;
        _service.Disconnected -= Service_Disconnected;
        await _service.DisposeAsync();
    }
}
