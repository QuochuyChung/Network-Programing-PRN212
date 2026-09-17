using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChatClient.Services;

public class ClientConfigService
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5000;
    public int FilePort { get; set; } = 5001;

    public static ClientConfigService Load()
    {
        var config = new ClientConfigService();
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Server", out var serverElem))
                {
                    if (serverElem.TryGetProperty("Host", out var hostElem) &&
                        !string.IsNullOrWhiteSpace(hostElem.GetString()))
                    {
                        config.Host = hostElem.GetString()!.Trim();
                    }

                    if (serverElem.TryGetProperty("Port", out var portElem) &&
                        portElem.TryGetInt32(out int port) &&
                        port > 0 && port <= 65535)
                    {
                        config.Port = port;
                    }

                    if (serverElem.TryGetProperty("FilePort", out var filePortElem) &&
                        filePortElem.TryGetInt32(out int filePort) &&
                        filePort > 0 && filePort <= 65535)
                    {
                        config.FilePort = filePort;
                    }
                    else
                    {
                        config.FilePort = config.Port + 1;
                    }
                }
            }
        }
        catch
        {
            // Fall back to default if reading fails
        }
        return config;
    }

    public void Save(string host, int port)
    {
        try
        {
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = (port > 0 && port <= 65535) ? port : 5000;

            JsonObject root;
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var serverNode = new JsonObject
            {
                ["Host"] = Host,
                ["Port"] = Port
            };
            root["Server"] = serverNode;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, root.ToJsonString(options));
        }
        catch
        {
            // Ignore error if file write permission denied
        }
    }
}
