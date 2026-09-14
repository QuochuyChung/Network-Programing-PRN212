using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatProtocol;

public static class FrameReader
{
    private const int MaxFrameSize = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) }
    };

    public static async Task<Message?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var header = new byte[sizeof(int)];
        var firstRead = await stream.ReadAsync(header.AsMemory(), cancellationToken);
        if (firstRead == 0)
        {
            return null;
        }

        await ReadRemainingAsync(stream, header, firstRead, cancellationToken);
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(header);
        if (payloadLength <= 0 || payloadLength > MaxFrameSize)
        {
            throw new InvalidDataException($"Invalid frame length: {payloadLength}.");
        }

        var payload = new byte[payloadLength];
        await ReadRemainingAsync(stream, payload, 0, cancellationToken);
        return JsonSerializer.Deserialize<Message>(payload, JsonOptions)
               ?? throw new InvalidDataException("The frame does not contain a valid message.");
    }

    private static async Task ReadRemainingAsync(Stream stream, byte[] buffer, int offset, CancellationToken cancellationToken)
    {
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("The connection closed in the middle of a frame.");
            }

            offset += bytesRead;
        }
    }
}
