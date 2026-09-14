# Network Programming PRN212 — TCP Chat App

Chat app nhiều nhóm (group chat), dựng bằng **TCP Socket thuần** (`System.Net.Sockets`) — không dùng SignalR/WebSocket. Server là Console App, Client là WPF App, dữ liệu lưu ở PostgreSQL.

## Tech stack

- .NET 8.0
- WPF (Client)
- Npgsql + Entity Framework Core (Server ↔ PostgreSQL)
- TCP Socket thuần (`TcpListener` / `TcpClient`)

## Cấu trúc project

```
ChatApp.sln
├── ChatProtocol/      Thư viện dùng chung (Server + Client đều tham chiếu)
├── ChatServer.Data/   Tầng persistence (EF Core + PostgreSQL)
├── ChatServer/        Console App — server trung tâm
└── ChatClient/        WPF App — giao diện người dùng
```

### `ChatProtocol`
| File | Vai trò |
|---|---|
| `MessageType.cs` | Enum liệt kê toàn bộ loại message (`LOGIN`, `MESSAGE`, `CREATE_GROUP`...) |
| `Message.cs` | Class chứa `Type`, `GroupId`, `Sender`, `Content`, `Timestamp` — gói tin chuẩn cả 2 bên dùng chung |
| `FrameReader.cs` / `FrameWriter.cs` | Đọc/ghi theo length-prefix framing (4 byte độ dài + payload) để tách đúng ranh giới 1 message trên dòng byte TCP |

### `ChatServer.Data`
| File | Vai trò |
|---|---|
| `Models/User.cs`, `Group.cs`, `GroupMember.cs`, `MessageRecord.cs` | Entity ánh xạ 1:1 tới 4 bảng trong PostgreSQL |
| `ChatDbContext.cs` | Mở kết nối DB, expose `DbSet<>` để query/insert |

### `ChatServer`
| File | Vai trò |
|---|---|
| `Program.cs` | Điểm khởi động: mở `TcpListener`, vòng lặp `AcceptTcpClient()` chờ client mới |
| `ClientHandler.cs` | 1 instance ứng với 1 client đang kết nối, chạy trên 1 thread riêng, `while(true)` đọc message liên tục |
| `GroupManager.cs` | Online Registry (`Dictionary<Username, Socket>`) + logic tra `GroupMembers`, định tuyến broadcast, xử lý `CREATE_GROUP`/`ADD_MEMBER` |

### `ChatClient`
| File | Vai trò |
|---|---|
| `LoginWindow.xaml(.cs)` | Nhập username |
| `GroupListWindow.xaml(.cs)` | Danh sách nhóm, tạo/chọn nhóm |
| `ChatWindow.xaml(.cs)` | Khung chat, sidebar thành viên, ô nhập + emoji |
| `ChatClientService.cs` | Lớp networking: mở `TcpClient`, thread nền đọc message liên tục, bắn event để UI cập nhật |

## Kiến trúc cốt lõi

- Mỗi client chỉ giữ **đúng 1 kết nối TCP** tới server, bất kể tham gia bao nhiêu nhóm.
- **Online Registry** (RAM, server) và **GroupMembers** (DB, vĩnh viễn) là 2 nguồn dữ liệu tách biệt — chỉ **đối chiếu** với nhau lúc cần định tuyến message hoặc vẽ sidebar, không đồng bộ/gộp vào nhau.
- Offline chỉ xoá khỏi Online Registry — không bao giờ xoá khỏi DB.

## Message Protocol

```
LOGIN            client → server, gửi username lúc mở app
LOGIN_OK         server → client, xác nhận đăng nhập thành công
CREATE_GROUP     client → server, tạo nhóm mới (Name)
ADD_MEMBER       client → server, thêm 1 username vào nhóm
GROUP_LIST       server → client, danh sách nhóm mà user này thuộc về
OPEN_GROUP       client → server, chọn 1 nhóm để bắt đầu chat
MEMBER_LIST      server → client, thành viên của nhóm vừa mở kèm trạng thái online
MESSAGE_HISTORY  server → client, tin nhắn cũ của đúng nhóm vừa mở (từ DB)
MESSAGE          2 chiều, kèm GroupId — nội dung chat trong 1 nhóm cụ thể
USER_ONLINE      server → all, 1 username vừa mở app (bật chấm xanh)
USER_OFFLINE     server → all, 1 username vừa mất kết nối (chấm chuyển xám)
ERROR            server → client, báo lỗi (vd trùng username, không phải thành viên nhóm)
```

Ví dụ 1 message (JSON, gửi kèm 4-byte độ dài phía trước):

```json
{
  "Type": "MESSAGE",
  "GroupId": "a1b2c3d4-5e6f-7890-abcd-ef1234567890",
  "Sender": "huy",
  "Content": "Hello mọi người 👋",
  "Timestamp": "2026-09-13T10:15:00Z"
}
```

## Database schema (PostgreSQL)

Dùng `UUID` làm khoá chính thay vì số tự tăng — Postgres 13+ có sẵn hàm `gen_random_uuid()`, không cần cài thêm extension (nếu Postgres cũ hơn 13 thì chạy `CREATE EXTENSION IF NOT EXISTS pgcrypto;` trước).

```sql
CREATE TABLE "Users" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Username" VARCHAR(50) UNIQUE NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE "Groups" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(100) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE "GroupMembers" (
    "GroupId" UUID NOT NULL REFERENCES "Groups"("Id"),
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "JoinedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("GroupId", "UserId")
);

CREATE TABLE "Messages" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "GroupId" UUID NOT NULL REFERENCES "Groups"("Id"),
    "Sender" VARCHAR(50) NOT NULL,
    "Content" TEXT NOT NULL,
    "SentAt" TIMESTAMP NOT NULL DEFAULT NOW()
);
```

## Luồng chạy — từ mở app tới gửi tin nhắn

```
1. Server: Program.cs → TcpListener.Start() → vòng lặp AcceptTcpClient()

2. Client: LoginWindow mở → nhập username → ChatClientService.Connect()
           → TcpClient.Connect(ip, port)   [bắt tay TCP]
           → gửi Message{Type=LOGIN, Sender=username}

3. Server: Program.cs nhận kết nối mới → tạo 1 ClientHandler
           → ClientHandler đọc được LOGIN
           → GroupManager: insert Users nếu chưa có, add vào Online Registry
           → gửi lại LOGIN_OK, broadcast USER_ONLINE cho người khác

4. Client: nhận LOGIN_OK → mở GroupListWindow → gửi GROUP_LIST

5. Server: GroupManager query GroupMembers (lọc theo user) → trả GROUP_LIST

6. Client: chọn 1 nhóm → gửi OPEN_GROUP{GroupId}

7. Server: GroupManager query GroupMembers + Messages của nhóm đó
           → trả MEMBER_LIST (kèm ai đang online) + MESSAGE_HISTORY

8. Client: mở ChatWindow → render sidebar + lịch sử tin nhắn cũ

9. User gõ tin nhắn (+ emoji) → bấm Send
           → ChatClientService gửi Message{Type=MESSAGE, GroupId, Content}

10. Server: ClientHandler đọc được MESSAGE
            → GroupManager: INSERT vào Messages (DB)
            → query GroupMembers → đối chiếu Online Registry
            → gửi (Write vào socket) cho từng người đang online trong nhóm

11. Các Client khác: thread nền trong ChatClientService đọc được MESSAGE
            → bắn event → ChatWindow (qua Dispatcher) thêm dòng chat mới lên UI

12. Khi 1 Client tắt app / rớt mạng:
            → ClientHandler bắt exception trong vòng lặp Read()
            → remove khỏi Online Registry, broadcast USER_OFFLINE
            → KHÔNG đụng gì tới DB
```

## Setup / chạy thử

```bash
git clone <repo-url>
cd SourceCode

# appsettings.json chứa password DB nên bị .gitignore, không có sẵn sau khi clone
# -> copy từ file mẫu rồi tự điền connection string của bạn vào (2 chỗ)
cp ChatServer.Data/appsettings.example.json ChatServer.Data/appsettings.json
cp ChatServer/appsettings.example.json ChatServer/appsettings.json
# rồi mở 2 file appsettings.json vừa tạo, sửa Password=... thành password thật

dotnet build

cd ChatServer.Data
dotnet ef database update
cd ..

# Chạy server
dotnet run --project ChatServer

# Chạy client (mở terminal khác)
dotnet run --project ChatClient
```

> **Lưu ý:** `appsettings.json` (chứa password DB thật) không được commit lên git. Mỗi người tự copy từ `appsettings.example.json` rồi điền password riêng của mình — xem `.gitignore`.
