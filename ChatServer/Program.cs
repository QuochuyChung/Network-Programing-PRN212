using System.Net;
using System.Net.Sockets;
using ChatServer;
using ChatServer.Data;

int port = ServerConfig.GetPort();
int filePort = ServerConfig.GetFilePort();

var fileServer = new ChatServer.Services.FileTransferServer(filePort);
fileServer.Start();

TcpListener listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"Server is running, listening on port {port} (File transfer on port {filePort})...");

while (true)
{
    TcpClient client = listener.AcceptTcpClient();
    Console.WriteLine("New client connected.");

    var handler = new ClientHandler(client);
    Thread thread = new Thread(handler.Run);
    thread.Start();
}
