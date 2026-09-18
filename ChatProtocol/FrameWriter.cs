using System.Text;
using System.Text.Json;

public static class FrameWriter
{
    public static void WriteMessage(Stream stream, Message message)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);

        stream.Write(lengthPrefix, 0, lengthPrefix.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    public static async Task WriteMessageAsync(Stream stream, Message message, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);

        await stream.WriteAsync(lengthPrefix.AsMemory(0, lengthPrefix.Length), cancellationToken);
        await stream.WriteAsync(payload.AsMemory(0, payload.Length), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
