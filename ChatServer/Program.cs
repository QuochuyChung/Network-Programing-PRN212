using System.Net;
using System.Net.Sockets;

TcpListener listener = new TcpListener(IPAddress.Any, 5000);
listener.Start();
Console.WriteLine("Server dang chay, cho ket noi tren port 5000...");

while (true)
{
    TcpClient client = listener.AcceptTcpClient();
    Console.WriteLine("Co client moi ket noi.");

    var handler = new ClientHandler(client);
    Thread thread = new Thread(handler.Run);
    thread.Start();
}
