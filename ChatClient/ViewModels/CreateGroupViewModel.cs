using System.Collections.ObjectModel;
using System.Windows.Input;
using ChatClient.Services;
using ChatClient.Utils;

namespace ChatClient.ViewModels;

public class SelectableUserItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public string Username { get; set; } = string.Empty;
    public bool IsOnline { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string AvatarLetter => string.IsNullOrWhiteSpace(Username) ? "?" : char.ToUpper(Username.Trim()[0]).ToString();
    public string AvatarColor => AvatarColorGenerator.GetColorForName(Username);
    public string StatusColor => IsOnline ? "#22C55E" : "#94A3B8";
}

public class CreateGroupViewModel : ViewModelBase
{
    private string _groupName = string.Empty;
    private string _customUsername = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasError;

    public ObservableCollection<SelectableUserItemViewModel> AvailableUsers { get; } = [];

    public event Action<string, List<string>>? GroupCreationConfirmed;
    public event Action? CloseRequested;

    public string GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value))
            {
                ClearError();
            }
        }
    }

    public string CustomUsername
    {
        get => _customUsername;
        set => SetProperty(ref _customUsername, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                HasError = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public ICommand CreateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddCustomUserCommand { get; }

    public CreateGroupViewModel()
    {
        CreateCommand = new RelayCommand(_ => ExecuteCreate());
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke());
        AddCustomUserCommand = new RelayCommand(_ => ExecuteAddCustomUser());

        LoadUsers();
    }

    private void LoadUsers()
    {
        var currentUsername = ChatClientService.Instance.CurrentUser;

        // Register to receive users
        ChatClientService.Instance.OnUserListReceived += HandleUserList;
        ChatClientService.Instance.RequestUserList();
    }

    private void HandleUserList(List<string> users)
    {
        ChatClientService.Instance.OnUserListReceived -= HandleUserList;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            AvailableUsers.Clear();
            var current = ChatClientService.Instance.CurrentUser;

            foreach (var u in users)
            {
                if (!string.Equals(u, current, StringComparison.OrdinalIgnoreCase))
                {
                    AvailableUsers.Add(new SelectableUserItemViewModel
                    {
                        Username = u,
                        IsOnline = false
                    });
                }
            }
        });
    }

    private void ExecuteAddCustomUser()
    {
        string trimmed = CustomUsername.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        var existing = AvailableUsers.FirstOrDefault(u => string.Equals(u.Username, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            var newItem = new SelectableUserItemViewModel
            {
                Username = trimmed,
                IsSelected = true
            };
            AvailableUsers.Insert(0, newItem);
        }
        else
        {
            existing.IsSelected = true;
        }

        CustomUsername = string.Empty;
    }

    private void ExecuteCreate()
    {
        string name = GroupName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Please enter a group name.";
            return;
        }

        var selectedMembers = AvailableUsers
            .Where(u => u.IsSelected)
            .Select(u => u.Username)
            .ToList();

        GroupCreationConfirmed?.Invoke(name, selectedMembers);
        CloseRequested?.Invoke();
    }

    private void ClearError()
    {
        if (HasError)
        {
            ErrorMessage = string.Empty;
            HasError = false;
        }
    }
}
