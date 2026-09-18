using ChatClient.Utils;

namespace ChatClient.ViewModels;

public class ConversationItemViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _lastMessage = string.Empty;
    private DateTime? _lastActivity;
    private int _unreadCount;
    private int _memberCount;
    private bool _isSelected;

    public Guid Id { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(AvatarLetter));
                OnPropertyChanged(nameof(AvatarColor));
            }
        }
    }

    public string LastMessage
    {
        get => _lastMessage;
        set => SetProperty(ref _lastMessage, value);
    }

    public DateTime? LastActivity
    {
        get => _lastActivity;
        set
        {
            if (SetProperty(ref _lastActivity, value))
            {
                OnPropertyChanged(nameof(FormattedTime));
            }
        }
    }

    public string FormattedTime
    {
        get
        {
            if (!LastActivity.HasValue) return string.Empty;
            var local = LastActivity.Value.ToLocalTime();
            var now = DateTime.Now;
            if (local.Date == now.Date)
            {
                return local.ToString("HH:mm");
            }
            if (local.Date == now.Date.AddDays(-1))
            {
                return "Yesterday";
            }
            return local.ToString("dd/MM");
        }
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (SetProperty(ref _unreadCount, value))
            {
                OnPropertyChanged(nameof(HasUnread));
                OnPropertyChanged(nameof(UnreadDisplay));
            }
        }
    }

    public bool HasUnread => UnreadCount > 0;
    public string UnreadDisplay => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public int MemberCount
    {
        get => _memberCount;
        set => SetProperty(ref _memberCount, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string AvatarLetter => string.IsNullOrWhiteSpace(Name) ? "?" : char.ToUpper(Name.Trim()[0]).ToString();
    public string AvatarColor => AvatarColorGenerator.GetColorForName(Name);
}
