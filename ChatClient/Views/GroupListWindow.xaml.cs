using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatClient.Services;
using ChatProtocol;

namespace ChatClient.Views;

/// <summary>
/// Màn hình sau đăng nhập: quản lý danh sách group và thêm thành viên.
/// Khu vực bên phải chỉ là placeholder để phần chat có thể được ghép vào sau.
/// </summary>
public partial class GroupListWindow : Window
{
    private readonly ChatClientService _service = ChatClientService.Instance;
    private readonly ObservableCollection<GroupListItem> _groups = new();
    private readonly ObservableCollection<MemberListItem> _members = new();

    public GroupListWindow()
    {
        InitializeComponent();
        GroupListBox.ItemsSource = _groups;
        MemberListBox.ItemsSource = _members;
        CurrentUserText.Text = $"@{_service.CurrentUser}";

        _service.OnMessageReceived += HandleServerMessage;
        _service.OnDisconnected += HandleDisconnected;

        // LOGIN_OK đã được xử lý ở LoginWindow; yêu cầu danh sách group ngay khi mở màn hình.
        Loaded += async (_, _) => await LoadGroupsAsync();
    }

    private async void OnCreateGroupClick(object sender, RoutedEventArgs e)
    {
        string groupName = GroupNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(groupName))
        {
            ShowStatus("Please enter a group name.", isError: true);
            GroupNameTextBox.Focus();
            return;
        }

        SetActionButtonsEnabled(false);
        bool sent = await _service.CreateGroupAsync(groupName);
        if (!sent) ShowStatus("Could not send the create-group request.", isError: true);
        SetActionButtonsEnabled(true);
    }

    private async void OnAddMemberClick(object sender, RoutedEventArgs e)
    {
        if (GroupListBox.SelectedItem is not GroupListItem selectedGroup)
        {
            ShowStatus("Please select a group first.", isError: true);
            return;
        }

        string username = MemberUsernameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowStatus("Please enter the username to add.", isError: true);
            MemberUsernameTextBox.Focus();
            return;
        }

        SetActionButtonsEnabled(false);
        bool sent = await _service.AddMemberAsync(selectedGroup.Id, username);
        if (!sent) ShowStatus("Could not send the add-member request.", isError: true);
        SetActionButtonsEnabled(true);
    }

    private async void OnRefreshGroupsClick(object sender, RoutedEventArgs e)
    {
        await LoadGroupsAsync();
    }

    private async void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = GroupListBox.SelectedItem is GroupListItem;
        MemberUsernameTextBox.IsEnabled = hasSelection;
        AddMemberButton.IsEnabled = hasSelection;

        if (GroupListBox.SelectedItem is GroupListItem selectedGroup)
        {
            SelectedGroupHint.Text = $"Adding to: {selectedGroup.Name}";
            ChatGroupTitle.Text = selectedGroup.Name;
            ChatGroupSubtitle.Text = selectedGroup.MemberSummary;
            ChatPlaceholderTitle.Text = selectedGroup.Name;

            // Xóa dữ liệu group cũ trong lúc chờ server trả MEMBER_LIST của group mới.
            _members.Clear();
            MemberCountText.Text = "Loading...";
            EmptyMembersText.Text = "Loading members...";
            EmptyMembersText.Visibility = Visibility.Visible;

            bool sent = await _service.OpenGroupAsync(selectedGroup.Id);
            if (!sent) ShowStatus("Could not open the selected group.", isError: true);
        }
        else
        {
            SelectedGroupHint.Text = "Select a group first";
            ChatGroupTitle.Text = "Select a group";
            ChatGroupSubtitle.Text = "Choose a group from the lower-left list";
            ChatPlaceholderTitle.Text = "Select a group to start";
            _members.Clear();
            MemberCountText.Text = "0";
            EmptyMembersText.Text = "Select a group to view members";
            EmptyMembersText.Visibility = Visibility.Visible;
        }
    }

    private async Task LoadGroupsAsync()
    {
        bool sent = await _service.RequestGroupListAsync();
        if (!sent) ShowStatus("Could not load groups because the server is disconnected.", isError: true);
    }

    private void HandleServerMessage(Message message)
    {
        // Network event chạy ở background thread; mọi cập nhật WPF phải về UI Dispatcher.
        Dispatcher.Invoke(() =>
        {
            switch (message.Type)
            {
                case MessageType.GROUP_LIST:
                    ApplyGroupList(message.Content);
                    break;
                case MessageType.CREATE_GROUP:
                    GroupNameTextBox.Clear();
                    ShowStatus(message.Content, isError: false);
                    break;
                case MessageType.ADD_MEMBER:
                    MemberUsernameTextBox.Clear();
                    ShowStatus(message.Content, isError: false);
                    break;
                case MessageType.MEMBER_LIST:
                    ApplyMemberList(message);
                    break;
                case MessageType.ERROR:
                    ShowStatus(message.Content, isError: true);
                    break;
            }
        });
    }

    private void ApplyMemberList(Message message)
    {
        // Bỏ qua response cũ nếu user đã chọn sang group khác trong lúc server xử lý.
        if (GroupListBox.SelectedItem is not GroupListItem selectedGroup ||
            selectedGroup.Id != message.GroupId)
        {
            return;
        }

        try
        {
            var receivedMembers = JsonSerializer.Deserialize<List<MemberListItem>>(message.Content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<MemberListItem>();

            _members.Clear();
            foreach (var member in receivedMembers) _members.Add(member);
            MemberCountText.Text = $"{_members.Count} member{(_members.Count == 1 ? string.Empty : "s")}";
            EmptyMembersText.Text = "This group has no members";
            EmptyMembersText.Visibility = _members.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (JsonException)
        {
            ShowStatus("The server returned an invalid member list.", isError: true);
        }
    }

    private void ApplyGroupList(string json)
    {
        try
        {
            Guid? selectedId = (GroupListBox.SelectedItem as GroupListItem)?.Id;
            var receivedGroups = JsonSerializer.Deserialize<List<GroupListItem>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<GroupListItem>();

            _groups.Clear();
            foreach (var group in receivedGroups) _groups.Add(group);
            EmptyGroupsText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Giữ group đang chọn sau khi refresh/member count thay đổi.
            GroupListBox.SelectedItem = selectedId.HasValue
                ? _groups.FirstOrDefault(group => group.Id == selectedId.Value)
                : null;
        }
        catch (JsonException)
        {
            ShowStatus("The server returned an invalid group list.", isError: true);
        }
    }

    private void HandleDisconnected()
    {
        Dispatcher.Invoke(() =>
        {
            SetActionButtonsEnabled(false);
            MemberUsernameTextBox.IsEnabled = false;
            ShowStatus("Disconnected from the server. Please reopen the app to connect again.", isError: true);
        });
    }

    private void SetActionButtonsEnabled(bool enabled)
    {
        CreateGroupButton.IsEnabled = enabled;
        AddMemberButton.IsEnabled = enabled && GroupListBox.SelectedItem != null;
    }

    private void ShowStatus(string content, bool isError)
    {
        StatusText.Text = content;
        StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            isError ? "#ED4956" : "#4ADE80"));
        StatusText.Visibility = Visibility.Visible;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        _service.OnMessageReceived -= HandleServerMessage;
        _service.OnDisconnected -= HandleDisconnected;
        _service.Disconnect();
    }

    public sealed class GroupListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public string MemberSummary => $"{MemberCount} member{(MemberCount == 1 ? string.Empty : "s")}";
    }

    public sealed class MemberListItem
    {
        public string Username { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public string Initial => string.IsNullOrWhiteSpace(Username)
            ? "?"
            : Username[..1].ToUpperInvariant();
        public string StatusText => IsOnline ? "Online" : "Offline";
    }
}
