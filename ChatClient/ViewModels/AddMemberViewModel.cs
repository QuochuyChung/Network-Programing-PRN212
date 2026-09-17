using System.Collections.ObjectModel;
using System.Windows.Input;
using ChatClient.Services;
using ChatClient.Utils;

namespace ChatClient.ViewModels;

public class AddMemberViewModel : ViewModelBase
{
    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasError;

    public Guid GroupId { get; }
    public string GroupName { get; }

    public ObservableCollection<SelectableUserItemViewModel> SuggestedUsers { get; } = [];

    public event Action<Guid, string>? MemberAddConfirmed;
    public event Action? CloseRequested;

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                ClearError();
            }
        }
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

    public ICommand AddCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectUserCommand { get; }

    public AddMemberViewModel(Guid groupId, string groupName)
    {
        GroupId = groupId;
        GroupName = groupName;

        AddCommand = new RelayCommand(_ => ExecuteAdd());
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke());
        SelectUserCommand = new RelayCommand(param =>
        {
            if (param is SelectableUserItemViewModel user)
            {
                Username = user.Username;
                ExecuteAdd();
            }
        });

        LoadSuggestedUsers();
    }

    private void LoadSuggestedUsers()
    {
        ChatClientService.Instance.OnUserListReceived += HandleUserList;
        ChatClientService.Instance.RequestUserList();
    }

    private void HandleUserList(List<string> users)
    {
        ChatClientService.Instance.OnUserListReceived -= HandleUserList;

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SuggestedUsers.Clear();
            var current = ChatClientService.Instance.CurrentUser;

            foreach (var u in users)
            {
                if (!string.Equals(u, current, StringComparison.OrdinalIgnoreCase))
                {
                    SuggestedUsers.Add(new SelectableUserItemViewModel
                    {
                        Username = u
                    });
                }
            }
        });
    }

    private void ExecuteAdd()
    {
        string trimmed = Username.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ErrorMessage = "Please enter a username to add.";
            return;
        }

        MemberAddConfirmed?.Invoke(GroupId, trimmed);
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
