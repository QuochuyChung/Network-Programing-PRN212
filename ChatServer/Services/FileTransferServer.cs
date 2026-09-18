using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ChatServer.Services;

public class FileTransferServer
{
    private readonly int _port;
    private readonly string _storageDir;
    private TcpListener? _listener;
    private bool _isRunning;

    private const byte CommandUpload = 1;
    private const byte CommandDownload = 2;
    private const int BufferSize = 64 * 1024; // 64 KB memory-safe streaming buffer

    public FileTransferServer(int port)
    {
        _port = port;
        _storageDir = Path.Combine(AppContext.BaseDirectory, "Storage", "uploads");

        if (!Directory.Exists(_storageDir))
        {
            Directory.CreateDirectory(_storageDir);
        }
    }

    public void Start()
    {
        _isRunning = true;
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        Console.WriteLine($"[FILE SERVER] Dedicated File Transfer Server is running on port {_port}...");
        Console.WriteLine($"[FILE SERVER] Storage directory: {_storageDir}");

        Task.Run(AcceptLoopAsync);
    }

    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (_isRunning && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (Exception)
            {
                if (!_isRunning) break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                int cmdByte = stream.ReadByte();
                if (cmdByte == -1) return;

                byte cmd = (byte)cmdByte;
                if (cmd == CommandUpload)
                {
                    await HandleUploadAsync(stream);
                }
                else if (cmd == CommandDownload)
                {
                    await HandleDownloadAsync(stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FILE SERVER ERROR] {ex.Message}");
            }
        }
    }

    private async Task HandleUploadAsync(NetworkStream stream)
    {
        // 1. Read FileId (16 bytes)
        byte[] guidBuffer = await ReadExactAsync(stream, 16);
        if (guidBuffer == null) return;
        var fileId = new Guid(guidBuffer);

        // 2. Read FileSize (8 bytes)
        byte[] sizeBuffer = await ReadExactAsync(stream, 8);
        if (sizeBuffer == null) return;
        long fileSize = BitConverter.ToInt64(sizeBuffer, 0);

        // 3. Read IsImage (1 byte)
        int isImageByte = stream.ReadByte();
        if (isImageByte == -1) return;
        bool isImage = isImageByte == 1;

        // 4. Read FileName
        byte[] nameLenBuffer = await ReadExactAsync(stream, 2);
        if (nameLenBuffer == null) return;
        short nameLen = BitConverter.ToInt16(nameLenBuffer, 0);

        byte[] nameBuffer = await ReadExactAsync(stream, nameLen);
        if (nameBuffer == null) return;
        string fileName = Encoding.UTF8.GetString(nameBuffer);
        string safeFileName = Path.GetFileName(fileName);

        string targetPath = Path.Combine(_storageDir, $"{fileId}_{safeFileName}");
        string tempPath = targetPath + ".tmp";

        Console.WriteLine($"[FILE UPLOAD START] FileId={fileId}, Name='{safeFileName}', Size={fileSize} bytes ({(fileSize / 1024.0 / 1024.0):F2} MB)");

        // 5. Stream chunks directly to disk
        byte[] buffer = new byte[BufferSize];
        long remaining = fileSize;

        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, toRead));
                if (bytesRead <= 0)
                {
                    throw new IOException("Client disconnected before file upload completed.");
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                remaining -= bytesRead;
            }
        }

        // Rename temp to target
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
        File.Move(tempPath, targetPath);

        // Send Ack (1 byte = Success)
        stream.WriteByte(1);
        await stream.FlushAsync();

        Console.WriteLine($"[FILE UPLOAD COMPLETE] Successfully saved '{safeFileName}' ({fileId})");
    }

    private async Task HandleDownloadAsync(NetworkStream stream)
    {
        // 1. Read FileId (16 bytes)
        byte[] guidBuffer = await ReadExactAsync(stream, 16);
        if (guidBuffer == null) return;
        var fileId = new Guid(guidBuffer);

        // 2. Find file on disk
        var matchingFiles = Directory.GetFiles(_storageDir, $"{fileId}_*");
        if (matchingFiles.Length == 0)
        {
            // File not found -> send -1L
            byte[] notFound = BitConverter.GetBytes(-1L);
            await stream.WriteAsync(notFound);
            await stream.FlushAsync();
            Console.WriteLine($"[FILE DOWNLOAD NOT FOUND] FileId={fileId}");
            return;
        }

        string filePath = matchingFiles[0];
        var fileInfo = new FileInfo(filePath);
        long fileSize = fileInfo.Length;
        string fileName = fileInfo.Name.Substring($"{fileId}_".Length);

        Console.WriteLine($"[FILE DOWNLOAD START] FileId={fileId}, Name='{fileName}', Size={fileSize} bytes");

        // 3. Send header
        byte[] sizeBuffer = BitConverter.GetBytes(fileSize);
        await stream.WriteAsync(sizeBuffer);

        byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
        byte[] nameLenBuffer = BitConverter.GetBytes((short)nameBytes.Length);
        await stream.WriteAsync(nameLenBuffer);
        await stream.WriteAsync(nameBytes);

        // 4. Stream file from disk to network
        byte[] buffer = new byte[BufferSize];
        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        {
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }
        }

        await stream.FlushAsync();
        Console.WriteLine($"[FILE DOWNLOAD COMPLETE] Successfully sent '{fileName}' ({fileId})");
    }

    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read <= 0) return null;
            offset += read;
        }
        return buffer;
    }
}
