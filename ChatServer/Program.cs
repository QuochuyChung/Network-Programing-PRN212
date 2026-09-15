using System.Net;
using System.Net.Sockets;
using ChatServer;
int port = ServerConfig.GetPort();
var groupManager = new GroupManager();
var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
Console.WriteLine($"Server is running, listening on port {port}...");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    Console.WriteLine("New client connected.");

    var handler = new ClientHandler(client, groupManager);
    _ = Task.Run(handler.RunAsync);
}
