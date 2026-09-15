using System.Text;
using System.Text.Json;

public static class FrameWriter
{
    /// <summary>
    /// Chuyển object message gửi lên từ client sang dạng byte để server nhận vì tin nhắn phải được gửi qua đường bytes nên là phải chuyển thành bytes
    /// Còn tiếp theo trong đây là gửi request cho server kèm theo số bytes mà quy định server phải đọc đúng 
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="message"></param>
    public static void WriteMessage(Stream stream, Message message)
    {
        // chuyển message thành json string 
        string json = JsonSerializer.Serialize(message);

        // chuyển thành dạng byte
        // JSON string → BYTES ← CHUYỂN SANG BYTES ✓
        // Ví dụ: [123, 34, 84, 121, ...]
        byte[] payload = Encoding.UTF8.GetBytes(json);

        // chuyển độ dài thành bytes 
        // số length -> bytes
        // 20 -> // Ví dụ: [20, 0, 0, 0]
        byte[] lengthPrefix = BitConverter.GetBytes(payload.Length);


        // gửi đi cho server
        stream.Write(lengthPrefix, 0, lengthPrefix.Length);
        // start = 0 -> end =  length
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }
}
