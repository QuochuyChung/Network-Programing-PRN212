namespace ChatProtocol;

/// <summary>
/// Một bộ message type dùng để điều phối request từ client
/// </summary>
public enum MessageType
{
    LOGIN,
    LOGIN_OK,
    CREATE_GROUP,
    ADD_MEMBER,
    GROUP_LIST,
    OPEN_GROUP,
    MEMBER_LIST,
    MESSAGE_HISTORY,
    MESSAGE,
    ONLINE_LIST,
    USER_ONLINE,
    USER_OFFLINE,
    USER_LIST,
    ERROR,
    FORCE_LOGOUT
}
