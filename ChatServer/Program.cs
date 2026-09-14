using System.Net;
using System.Net.Sockets;
using ChatServer;
using ChatServer.Data;
using Microsoft.EntityFrameworkCore;

var port = int.TryParse(Environment.GetEnvironmentVariable("CHAT_SERVER_PORT"), out var configuredPort)
    ? configuredPort
    : 5000;

try
{
    await using var db = new ChatDbContext();
    await db.Database.MigrateAsync();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Cannot initialize the database: {exception.Message}");
    Console.Error.WriteLine("Create ChatServer/appsettings.json from appsettings.example.json, then check PostgreSQL.");
    return;
}

var listener = new TcpListener(IPAddress.Any, port);
var groupManager = new GroupManager();
listener.Start();
Console.WriteLine($"Chat server is listening on 0.0.0.0:{port}.");

while (true)
{
    var tcpClient = await listener.AcceptTcpClientAsync();
    var handler = new ClientHandler(tcpClient, groupManager);
    _ = handler.RunAsync();
}
