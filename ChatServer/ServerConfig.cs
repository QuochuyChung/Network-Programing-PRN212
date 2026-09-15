using System.Text.Json;

namespace ChatServer;

public static class ServerConfig
{
    public const int DefaultPort = 5000;

    public static int GetPort()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path))
            {
                return DefaultPort;
            }

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Server", out var serverElem) &&
                serverElem.TryGetProperty("Port", out var portElem) &&
                portElem.TryGetInt32(out int port))
            {
                if (port > 0 && port <= 65535)
                {
                    return port;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIG WARN] Error reading Server:Port from appsettings.json ({ex.Message}), using default port {DefaultPort}.");
        }

        return DefaultPort;
    }
}
