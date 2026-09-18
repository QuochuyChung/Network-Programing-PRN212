using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ChatClient.Services;
using ChatClient.Utils;
using ChatProtocol;
using ChatProtocol.Dtos;
using Microsoft.Win32;

namespace ChatClient.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly Dispatcher _dispatcher;
    private readonly ChatClientService _clientService;

    private ConversationItemViewModel? _selectedConversation;
    private ObservableCollection<ChatMessageViewModel> _currentMessages = [];
    private ObservableCollection<MemberItemViewModel> _currentGroupMembers = [];
    private string _messageInput = string.Empty;
    private bool _isGroupSelected;
    private bool _isLoadingHistory;
    private bool _isMemberListOpen = false;

    // In-memory cache per group to ensure instantaneous switching (0ms, 60fps)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ObservableCollection<ChatMessageViewModel>> _groupMessagesCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ObservableCollection<MemberItemViewModel>> _groupMembersCache = new();
    private readonly HashSet<Guid> _loadedGroupHistories = new();

    // Toast notification properties
    private string _toastTitle = string.Empty;
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;
    private DispatcherTimer? _toastTimer;

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];

    public ObservableCollection<ChatMessageViewModel> CurrentMessages
    {
        get => _currentMessages;
        private set => SetProperty(ref _currentMessages, value);
    }

    public ObservableCollection<MemberItemViewModel> CurrentGroupMembers
    {
        get => _currentGroupMembers;
        private set => SetProperty(ref _currentGroupMembers, value);
    }

    public ObservableCollection<string> OnlineUsers { get; } = [];

    public string CurrentUsername => _clientService.CurrentUser ?? "User";
    public string CurrentUserAvatarLetter => string.IsNullOrWhiteSpace(CurrentUsername) ? "?" : char.ToUpper(CurrentUsername.Trim()[0]).ToString();
    public string CurrentUserAvatarColor => AvatarColorGenerator.GetColorForName(CurrentUsername);

    public event Action? ScrollToBottomRequested;
    public event Action<string>? ForceLogoutRequested;
    public event Action? LogoutRequested;
    public event Action? OpenCreateGroupDialogRequested;
    public event Action<Guid, string>? OpenAddMemberDialogRequested;
    public event Action<FileAttachmentDto, ImageSource?>? RequestOpenImageViewer;

    public ConversationItemViewModel? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (_selectedConversation == value && _selectedConversation != null)
                return;

            if (_selectedConversation != null)
            {
                _selectedConversation.IsSelected = false;
            }

            _selectedConversation = value;
            OnPropertyChanged(nameof(SelectedConversation));

            if (_selectedConversation != null)
            {
                _selectedConversation.IsSelected = true;
                _selectedConversation.UnreadCount = 0;
                IsGroupSelected = true;

                var gid = _selectedConversation.Id;

                // 1. Instant Cache Hit: Swap message collection immediately (0ms)
                if (_groupMessagesCache.TryGetValue(gid, out var cachedMsgs))
                {
                    CurrentMessages = cachedMsgs;
                    IsLoadingHistory = !_loadedGroupHistories.Contains(gid);
                }
                else
                {
                    var newMsgs = new ObservableCollection<ChatMessageViewModel>();
                    _groupMessagesCache[gid] = newMsgs;
                    CurrentMessages = newMsgs;
                    IsLoadingHistory = true;
                }

                // 2. Instant Member Cache Hit
                if (_groupMembersCache.TryGetValue(gid, out var cachedMembers))
                {
                    CurrentGroupMembers = cachedMembers;
                }
                else
                {
                    var newMembers = new ObservableCollection<MemberItemViewModel>();
                    _groupMembersCache[gid] = newMembers;
                    CurrentGroupMembers = newMembers;
                }

                // 3. Only query server if this group history hasn't been fetched yet
                if (!_loadedGroupHistories.Contains(gid))
                {
                    _clientService.OpenGroup(gid);
                }
                else
                {
                    ScrollToBottomRequested?.Invoke();
                }
            }
            else
            {
                IsGroupSelected = false;
            }
        }
    }

    public string MessageInput
    {
        get => _messageInput;
        set => SetProperty(ref _messageInput, value);
    }

    public bool IsGroupSelected
    {
        get => _isGroupSelected;
        set => SetProperty(ref _isGroupSelected, value);
    }

    public bool IsLoadingHistory
    {
        get => _isLoadingHistory;
        set => SetProperty(ref _isLoadingHistory, value);
    }

    public bool IsMemberListOpen
    {
        get => _isMemberListOpen;
        set => SetProperty(ref _isMemberListOpen, value);
    }

    public string ToastTitle
    {
        get => _toastTitle;
        set => SetProperty(ref _toastTitle, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        set => SetProperty(ref _isToastVisible, value);
    }

    // Commands
    public ICommand SendMessageCommand { get; }
    public ICommand SendImageCommand { get; }
    public ICommand SendFileCommand { get; }
    public ICommand SelectConversationCommand { get; }
    public ICommand OpenCreateGroupCommand { get; }
    public ICommand OpenAddMemberCommand { get; }
    public ICommand ToggleMemberListCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand CloseToastCommand { get; }
    public ICommand InsertEmojiCommand { get; }

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _clientService = ChatClientService.Instance;

        SendMessageCommand = new RelayCommand(async _ => await ExecuteSendMessageAsync(), _ => CanSendMessage());
        SendImageCommand = new RelayCommand(_ => ExecuteSendImages(), _ => IsGroupSelected);
        SendFileCommand = new RelayCommand(_ => ExecuteSendFile(), _ => IsGroupSelected);

        SelectConversationCommand = new RelayCommand(param =>
        {
            if (param is ConversationItemViewModel conv)
            {
                if (SelectedConversation == conv)
                {
                    IsGroupSelected = true;
                    if (!_loadedGroupHistories.Contains(conv.Id))
                    {
                        IsLoadingHistory = true;
                        _clientService.OpenGroup(conv.Id);
                    }
                }
                else
                {
                    SelectedConversation = conv;
                }
            }
        });

        OpenCreateGroupCommand = new RelayCommand(_ => OpenCreateGroupDialogRequested?.Invoke());
        OpenAddMemberCommand = new RelayCommand(_ =>
        {
            if (SelectedConversation != null)
            {
                OpenAddMemberDialogRequested?.Invoke(SelectedConversation.Id, SelectedConversation.Name);
            }
        }, _ => IsGroupSelected);

        ToggleMemberListCommand = new RelayCommand(_ => IsMemberListOpen = !IsMemberListOpen);
        LogoutCommand = new RelayCommand(_ => ExecuteLogout());
        CloseToastCommand = new RelayCommand(_ => IsToastVisible = false);
        InsertEmojiCommand = new RelayCommand(param =>
        {
            if (param is string emoji)
            {
                InsertEmoji(emoji);
            }
        });

        RegisterEvents();

        // Initial request to load groups
        _clientService.RequestGroupList();
    }

    private void RegisterEvents()
    {
        _clientService.OnGroupListReceived += HandleGroupListReceived;
        _clientService.OnGroupCreated += HandleGroupCreated;
        _clientService.OnMemberListReceived += HandleMemberListReceived;
        _clientService.OnMessageHistoryReceived += HandleMessageHistoryReceived;
        _clientService.OnChatMessageReceived += HandleChatMessageReceived;
        _clientService.OnOnlineListReceived += HandleOnlineListReceived;
        _clientService.OnUserOnline += HandleUserOnline;
        _clientService.OnUserOffline += HandleUserOffline;
        _clientService.OnForceLogout += HandleForceLogout;
        _clientService.OnErrorMessageReceived += HandleErrorMessage;
    }

    public void UnregisterEvents()
    {
        _clientService.OnGroupListReceived -= HandleGroupListReceived;
        _clientService.OnGroupCreated -= HandleGroupCreated;
        _clientService.OnMemberListReceived -= HandleMemberListReceived;
        _clientService.OnMessageHistoryReceived -= HandleMessageHistoryReceived;
        _clientService.OnChatMessageReceived -= HandleChatMessageReceived;
        _clientService.OnOnlineListReceived -= HandleOnlineListReceived;
        _clientService.OnUserOnline -= HandleUserOnline;
        _clientService.OnUserOffline -= HandleUserOffline;
        _clientService.OnForceLogout -= HandleForceLogout;
        _clientService.OnErrorMessageReceived -= HandleErrorMessage;
    }

    private bool CanSendMessage()
    {
        return IsGroupSelected && !string.IsNullOrWhiteSpace(MessageInput);
    }

    private async Task ExecuteSendMessageAsync()
    {
        if (SelectedConversation == null || string.IsNullOrWhiteSpace(MessageInput))
            return;

        string content = MessageInput.Trim();
        MessageInput = string.Empty;

        await _clientService.SendMessageAsync(SelectedConversation.Id, content);
    }

    private void ExecuteSendImages()
    {
        try
        {
            if (SelectedConversation == null) return;
            var groupId = SelectedConversation.Id;

            var openDialog = new OpenFileDialog
            {
                Title = "Select Image(s)",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All Files (*.*)|*.*",
                Multiselect = true
            };

            var owner = Application.Current?.MainWindow;
            bool? result = owner != null ? openDialog.ShowDialog(owner) : openDialog.ShowDialog();
            if (result != true) return;

            foreach (var filePath in openDialog.FileNames)
            {
                UploadAndSendFileAsync(groupId, filePath, isImage: true);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Image Error", $"Cannot open image: {ex.Message}");
        }
    }

    private void ExecuteSendFile()
    {
        try
        {
            if (SelectedConversation == null) return;
            var groupId = SelectedConversation.Id;

            var openDialog = new OpenFileDialog
            {
                Title = "Select File to Send (≥ 500MB supported)",
                Filter = "All Files (*.*)|*.*",
                Multiselect = false
            };

            var owner = Application.Current?.MainWindow;
            bool? result = owner != null ? openDialog.ShowDialog(owner) : openDialog.ShowDialog();
            if (result != true) return;

            UploadAndSendFileAsync(groupId, openDialog.FileName, isImage: false);
        }
        catch (Exception ex)
        {
            ShowToast("File Error", $"Cannot open file: {ex.Message}");
        }
    }

    private void UploadAndSendFileAsync(Guid groupId, string filePath, bool isImage)
    {
        try
        {
            string fileName = Path.GetFileName(filePath);
            long fileSize = new FileInfo(filePath).Length;

            var attachmentDto = new FileAttachmentDto
            {
                FileName = fileName,
                FileSize = fileSize,
                IsImage = isImage,
                LocalFilePath = filePath
            };

            var placeholder = new ChatMessageViewModel
            {
                GroupId = groupId,
                Sender = CurrentUsername,
                Content = fileName,
                Timestamp = DateTime.UtcNow,
                IsMine = true,
                IsUploading = true,
                UploadProgress = 0,
                ProgressText = "0%",
                IsImage = isImage,
                IsFile = !isImage,
                Attachment = attachmentDto
            };

            if (isImage)
            {
                placeholder.LoadThumbnail(attachmentDto);
            }

            placeholder.PreviewRequested += (att, thumb) => RequestOpenImageViewer?.Invoke(att, thumb);

            // Chup lai dung collection cua group nay ngay luc tao placeholder.
            // KHONG duoc dung lai property "CurrentMessages" o buoc remove ben duoi,
            // vi CurrentMessages co the doi sang nhom khac trong luc file dang upload
            // (file lon >=500MB co the mat vai phut) -> remove nham cho, gay ket dinh
            // placeholder + nhan doi tin nhan that khi quay lai dung nhom.
            var targetCollection = CurrentMessages;
            targetCollection.Add(placeholder);
            ScrollToBottomRequested?.Invoke();

            Task.Run(async () =>
            {
                var cts = new CancellationTokenSource();
                placeholder.UploadCts = cts;

                var progress = new Progress<(long sent, long total)>(p =>
                {
                    double pct = p.total > 0 ? (double)p.sent / p.total * 100.0 : 0;
                    placeholder.UploadProgress = pct;
                    placeholder.ProgressText = $"{pct:F0}% ({(p.sent / 1024.0 / 1024.0):F1} MB / {(p.total / 1024.0 / 1024.0):F1} MB)";
                });

                try
                {
                    var attachment = await FileTransferService.Instance.UploadFileAsync(filePath, isImage, progress, cts.Token);
                    string json = JsonSerializer.Serialize(attachment);
                    string content = $"[ATTACHMENT]:{json}";

                    await _clientService.SendMessageAsync(groupId, content);

                    await _dispatcher.InvokeAsync(() =>
                    {
                        targetCollection.Remove(placeholder);
                    });
                }
                catch (OperationCanceledException)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        placeholder.IsUploading = false;
                        placeholder.ProgressText = "Upload cancelled.";
                    });
                }
                catch (Exception ex)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        placeholder.IsUploading = false;
                        placeholder.ProgressText = $"Upload failed: {ex.Message}";
                    });
                }
            });
        }
        catch (Exception ex)
        {
            ShowToast("Upload Error", $"Cannot prepare upload: {ex.Message}");
        }
    }

    public void InsertEmoji(string emoji)
    {
        MessageInput += emoji;
    }

    private void ExecuteLogout()
    {
        UnregisterEvents();
        _clientService.Disconnect();
        _groupMessagesCache.Clear();
        _groupMembersCache.Clear();
        _loadedGroupHistories.Clear();
        LogoutRequested?.Invoke();
    }

    private void HandleGroupListReceived(List<GroupDto> groupDtos)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var currentSelectedId = SelectedConversation?.Id;
            var incomingIds = groupDtos.Select(g => g.Id).ToHashSet();

            // 1. Remove deleted groups in reverse
            for (int i = Conversations.Count - 1; i >= 0; i--)
            {
                if (!incomingIds.Contains(Conversations[i].Id))
                {
                    Conversations.RemoveAt(i);
                }
            }

            // 2. Update existing or insert new without clearing collection
            foreach (var dto in groupDtos)
            {
                var existing = Conversations.FirstOrDefault(c => c.Id == dto.Id);
                if (existing != null)
                {
                    existing.Name = dto.Name;
                    existing.LastMessage = FormatPreviewText(dto.LastMessage);
                    existing.LastActivity = dto.LastActivity;
                    existing.MemberCount = dto.MemberCount;
                }
                else
                {
                    Conversations.Add(new ConversationItemViewModel
                    {
                        Id = dto.Id,
                        Name = dto.Name,
                        LastMessage = FormatPreviewText(dto.LastMessage),
                        LastActivity = dto.LastActivity,
                        MemberCount = dto.MemberCount
                    });
                }
            }

            // 3. Maintain active conversation selection without resetting or kicking user out
            if (currentSelectedId.HasValue)
            {
                var match = Conversations.FirstOrDefault(c => c.Id == currentSelectedId.Value);
                if (match != null)
                {
                    match.IsSelected = true;
                    _selectedConversation = match;
                    IsGroupSelected = true;
                    OnPropertyChanged(nameof(SelectedConversation));
                    OnPropertyChanged(nameof(IsGroupSelected));

                    if (!_loadedGroupHistories.Contains(match.Id))
                    {
                        IsLoadingHistory = true;
                        _clientService.OpenGroup(match.Id);
                    }
                }
            }
            else if (Conversations.Count > 0 && SelectedConversation == null)
            {
                SelectedConversation = Conversations[0];
            }
        });
    }

    private void HandleGroupCreated(GroupDto createdGroup)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var item = new ConversationItemViewModel
            {
                Id = createdGroup.Id,
                Name = createdGroup.Name,
                LastMessage = FormatPreviewText(createdGroup.LastMessage),
                LastActivity = createdGroup.LastActivity,
                MemberCount = createdGroup.MemberCount
            };

            Conversations.Insert(0, item);
            SelectedConversation = item;
            ShowToast("Group Created", $"You created group '{createdGroup.Name}'");
        });
    }

    private void HandleMemberListReceived(Guid groupId, List<GroupMemberDto> members)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var newMembers = new ObservableCollection<MemberItemViewModel>();
            foreach (var m in members.OrderByDescending(x => x.IsOnline).ThenBy(x => x.Username))
            {
                newMembers.Add(new MemberItemViewModel
                {
                    Username = m.Username,
                    IsOnline = m.IsOnline
                });
            }

            _groupMembersCache[groupId] = newMembers;

            var conv = Conversations.FirstOrDefault(c => c.Id == groupId);
            if (conv != null)
            {
                conv.MemberCount = members.Count;
            }

            if (SelectedConversation?.Id == groupId)
            {
                CurrentGroupMembers = newMembers;
            }
        });
    }

    private void HandleMessageHistoryReceived(Guid groupId, List<MessageHistoryItemDto> history)
    {
        // Parse history off the UI thread to prevent UI freezing
        Task.Run(() =>
        {
            var parsedList = new List<ChatMessageViewModel>(history.Count);
            foreach (var h in history)
            {
                try
                {
                    var msgVm = new ChatMessageViewModel
                    {
                        Id = h.Id,
                        GroupId = h.GroupId,
                        Sender = h.Sender,
                        Content = h.Content,
                        Timestamp = h.SentAt,
                        IsMine = string.Equals(h.Sender, CurrentUsername, StringComparison.OrdinalIgnoreCase)
                    };
                    parsedList.Add(msgVm);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HISTORY ITEM PARSE ERROR] {ex.Message}");
                }
            }

            _dispatcher.BeginInvoke(() =>
            {
                _loadedGroupHistories.Add(groupId);

                var newGroupMsgs = new ObservableCollection<ChatMessageViewModel>();
                foreach (var vm in parsedList)
                {
                    vm.PreviewRequested += (att, thumb) => RequestOpenImageViewer?.Invoke(att, thumb);
                    newGroupMsgs.Add(vm);
                }

                _groupMessagesCache[groupId] = newGroupMsgs;

                if (SelectedConversation?.Id == groupId)
                {
                    CurrentMessages = newGroupMsgs;
                    IsLoadingHistory = false;
                    ScrollToBottomRequested?.Invoke();
                }
            });
        });
    }

    private void HandleChatMessageReceived(Message msg)
    {
        _dispatcher.BeginInvoke(() =>
        {
            bool isCurrentGroup = SelectedConversation?.Id == msg.GroupId;

            var chatMsg = new ChatMessageViewModel
            {
                GroupId = msg.GroupId,
                Sender = msg.Sender,
                Content = msg.Content,
                Timestamp = msg.Timestamp,
                IsMine = string.Equals(msg.Sender, CurrentUsername, StringComparison.OrdinalIgnoreCase)
            };
            chatMsg.PreviewRequested += (att, thumb) => RequestOpenImageViewer?.Invoke(att, thumb);

            // Always add to the group's in-memory cached collection
            if (!_groupMessagesCache.TryGetValue(msg.GroupId, out var groupMsgs))
            {
                groupMsgs = new ObservableCollection<ChatMessageViewModel>();
                _groupMessagesCache[msg.GroupId] = groupMsgs;
            }
            groupMsgs.Add(chatMsg);

            if (isCurrentGroup)
            {
                ScrollToBottomRequested?.Invoke();

                if (SelectedConversation != null)
                {
                    SelectedConversation.LastMessage = FormatPreviewText(msg.Sender, msg.Content);
                    SelectedConversation.LastActivity = msg.Timestamp;
                    MoveConversationToTop(SelectedConversation);
                }
            }
            else
            {
                var conv = Conversations.FirstOrDefault(c => c.Id == msg.GroupId);
                if (conv != null)
                {
                    conv.UnreadCount++;
                    conv.LastMessage = FormatPreviewText(msg.Sender, msg.Content);
                    conv.LastActivity = msg.Timestamp;
                    MoveConversationToTop(conv);

                    ShowToast(conv.Name, FormatPreviewText(msg.Sender, msg.Content));
                }
                else
                {
                    _clientService.RequestGroupList();
                    ShowToast("New Message", FormatPreviewText(msg.Sender, msg.Content));
                }
            }
        });
    }

    private string FormatPreviewText(string? content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        if (content.StartsWith("[ATTACHMENT]:", StringComparison.Ordinal))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<FileAttachmentDto>(content.Substring("[ATTACHMENT]:".Length));
                if (dto != null)
                {
                    return dto.IsImage ? "📷 Photo" : $"📎 {dto.FileName}";
                }
            }
            catch { }
            return "📎 Attachment";
        }
        return content;
    }

    private string FormatPreviewText(string sender, string content)
    {
        string prefix = string.Equals(sender, CurrentUsername, StringComparison.OrdinalIgnoreCase) ? "You: " : $"{sender}: ";
        return prefix + FormatPreviewText(content);
    }

    private void MoveConversationToTop(ConversationItemViewModel item)
    {
        int currentIndex = Conversations.IndexOf(item);
        if (currentIndex > 0)
        {
            Conversations.Move(currentIndex, 0);
        }
    }

    private void HandleOnlineListReceived(List<string> users)
    {
        _dispatcher.BeginInvoke(() =>
        {
            OnlineUsers.Clear();
            foreach (var u in users)
            {
                if (!string.Equals(u, CurrentUsername, StringComparison.OrdinalIgnoreCase))
                {
                    OnlineUsers.Add(u);
                }
            }
        });
    }

    private void HandleUserOnline(string username)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (!string.Equals(username, CurrentUsername, StringComparison.OrdinalIgnoreCase) && !OnlineUsers.Contains(username))
            {
                OnlineUsers.Add(username);
            }

            var member = CurrentGroupMembers.FirstOrDefault(m => string.Equals(m.Username, username, StringComparison.OrdinalIgnoreCase));
            if (member != null)
            {
                member.IsOnline = true;
            }
        });
    }

    private void HandleUserOffline(string username)
    {
        _dispatcher.BeginInvoke(() =>
        {
            OnlineUsers.Remove(username);

            var member = CurrentGroupMembers.FirstOrDefault(m => string.Equals(m.Username, username, StringComparison.OrdinalIgnoreCase));
            if (member != null)
            {
                member.IsOnline = false;
            }
        });
    }

    private void HandleForceLogout(string reason)
    {
        _dispatcher.BeginInvoke(() =>
        {
            UnregisterEvents();
            ForceLogoutRequested?.Invoke(reason);
        });
    }

    private void HandleErrorMessage(string error)
    {
        _dispatcher.BeginInvoke(() =>
        {
            ShowToast("Error", error);
        });
    }

    public void ShowToast(string title, string message)
    {
        ToastTitle = title;
        ToastMessage = message;
        IsToastVisible = true;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };
        _toastTimer.Start();
    }
}
