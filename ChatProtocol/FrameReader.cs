using System.Text;
using System.Text.Json;

namespace ChatProtocol;

public static class FrameReader
{
    private const int MaxFrameLength = 16 * 1024 * 1024;

    public static Message? ReadMessage(Stream stream)
    {
        /*
            Client gửi: [20,0,0,0] [123,34,84,121,...20 bytes...]

            Server ReadMessage():
            
            1. ReadExact(4) → đọc [20,0,0,0]
            2. BitConverter.ToInt32() → 20
            3. ReadExact(20) → đọc [123,34,84,121,...]
            4. GetString() → "{"Type":1,"Data":"Hi"}"
            5. Deserialize() → Message object ✓
        */
        byte[]? lengthBuffer = ReadExact(stream, 4);
        if (lengthBuffer == null) return null; // ket noi da dong

        int length = BitConverter.ToInt32(lengthBuffer, 0);
        ValidateLength(length);

        byte[]? payload = ReadExact(stream, length);
        if (payload == null) return null;

        string json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<Message>(json);
    }

    public static async Task<Message?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[]? lengthBuffer = await ReadExactAsync(stream, 4, cancellationToken);
        if (lengthBuffer is null)
        {
            return null;
        }

        int length = BitConverter.ToInt32(lengthBuffer, 0);
        ValidateLength(length);

        byte[]? payload = await ReadExactAsync(stream, length, cancellationToken);
        return payload is null ? null : JsonSerializer.Deserialize<Message>(payload);
    }

    // stream.Read() khong dam bao doc du "count" byte trong 1 lan goi,
    // nen phai lap lai cho toi khi doc du, khong thi bi lech frame.
    private static byte[]? ReadExact(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int bytesRead = stream.Read(buffer, offset, count - offset);
            if (bytesRead == 0) return null; // ket noi bi dong giua chung
            offset += bytesRead;
        }

        return buffer;
    }

    private static async Task<byte[]?> ReadExactAsync(
        Stream stream,
        int count,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int bytesRead = await stream.ReadAsync(
                buffer.AsMemory(offset, count - offset),
                cancellationToken);
            if (bytesRead == 0)
            {
                return null;
            }

            offset += bytesRead;
        }

        return buffer;
    }

    private static void ValidateLength(int length)
    {
        if (length <= 0 || length > MaxFrameLength)
        {
            throw new InvalidDataException($"Độ dài frame không hợp lệ: {length} byte.");
        }
    }
}
