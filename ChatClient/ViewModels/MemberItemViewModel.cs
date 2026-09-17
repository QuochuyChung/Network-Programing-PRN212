using ChatClient.Utils;

namespace ChatClient.ViewModels;

public class MemberItemViewModel : ViewModelBase
{
    private string _username = string.Empty;
    private bool _isOnline;

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                OnPropertyChanged(nameof(AvatarLetter));
                OnPropertyChanged(nameof(AvatarColor));
            }
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (SetProperty(ref _isOnline, value))
            {
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusColor => IsOnline ? "#22C55E" : "#94A3B8";
    public string StatusText => IsOnline ? "Online" : "Offline";

    public string AvatarLetter => string.IsNullOrWhiteSpace(Username) ? "?" : char.ToUpper(Username.Trim()[0]).ToString();
    public string AvatarColor => AvatarColorGenerator.GetColorForName(Username);
}
