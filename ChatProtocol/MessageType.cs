namespace ChatProtocol;

public enum MessageType
{
    Login,
    LoginOk,
    CreateGroup,
    AddMember,
    GroupList,
    OpenGroup,
    MemberList,
    MessageHistory,
    Message,
    UserOnline,
    UserOffline,
    Error
}
