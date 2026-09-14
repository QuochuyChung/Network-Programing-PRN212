using System.Text;
using System.Text.Json;

namespace ChatProtocol;

public static class FrameReader
{
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

        byte[]? payload = ReadExact(stream, length);
        if (payload == null) return null;

        string json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<Message>(json);
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
}
