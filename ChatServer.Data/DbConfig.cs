using System.Text.Json;

namespace ChatServer.Data;

// Doc connection string tu appsettings.json nam cung thu muc voi file .exe/.dll
// dang chay (AppContext.BaseDirectory). Ca ChatServer.Data va ChatServer deu
// can co 1 ban appsettings.json rieng trong thu muc project cua minh, vi
// content file khong tu dong "chay theo" project reference sang project khac.
public static class DbConfig
{
    public static string GetConnectionString()
    {
        var environmentValue = Environment.GetEnvironmentVariable("CHATAPP_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "Không tìm thấy appsettings.json. Hãy copy appsettings.example.json thành " +
                "appsettings.json hoặc đặt biến môi trường CHATAPP_CONNECTION_STRING.");
        }

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("ChatDb")
            .GetString()!;
    }
}
