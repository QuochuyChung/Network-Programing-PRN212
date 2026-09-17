using System.Windows.Input;
using ChatClient.Services;

namespace ChatClient.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasError;
    private bool _isLoading;

    private readonly ClientConfigService _config;

    public event Action<string>? LoginSuccess;

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

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ButtonText));
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public string ButtonText => IsLoading ? "Connecting..." : "Join Chat";
    public bool CanSubmit => !IsLoading;

    public ICommand LoginCommand { get; }

    public LoginViewModel(string? initialError = null)
    {
        _config = ClientConfigService.Load();
        LoginCommand = new RelayCommand(async _ => await ExecuteLoginAsync(), _ => CanSubmit);

        if (!string.IsNullOrWhiteSpace(initialError))
        {
            ErrorMessage = initialError;
        }
    }

    private async Task ExecuteLoginAsync()
    {
        string trimmed = Username.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            ErrorMessage = "Please enter your username / nickname to join.";
            return;
        }

        IsLoading = true;
        ClearError();

        var (success, message) = await ChatClientService.Instance.ConnectAndLoginAsync(
            _config.Host,
            _config.Port,
            trimmed);

        IsLoading = false;

        if (success)
        {
            LoginSuccess?.Invoke(ChatClientService.Instance.CurrentUser ?? trimmed);
        }
        else
        {
            ErrorMessage = message;
        }
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
