using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatProtocol;

public static class FrameWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) }
    };

    public static async Task WriteAsync(Stream stream, Message message, SemaphoreSlim writeLock, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
