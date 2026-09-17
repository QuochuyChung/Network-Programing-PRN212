using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using ChatProtocol.Dtos;

namespace ChatClient.Services;

public class FileTransferService
{
    private static FileTransferService? _instance;
    public static FileTransferService Instance => _instance ??= new FileTransferService();

    private readonly ClientConfigService _config;
    private const byte CommandUpload = 1;
    private const byte CommandDownload = 2;
    private const int BufferSize = 64 * 1024; // 64 KB memory-safe chunk buffer

    public FileTransferService()
    {
        _config = ClientConfigService.Load();
    }

    public async Task<FileAttachmentDto> UploadFileAsync(
        string filePath,
        bool isImage,
        IProgress<(long sent, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("File not found.", filePath);
        }

        long fileSize = fileInfo.Length;
        string fileName = fileInfo.Name;
        var fileId = Guid.NewGuid();

        string? thumbnailBase64 = null;
        if (isImage)
        {
            thumbnailBase64 = GenerateThumbnailBase64(filePath);
        }

        using var client = new TcpClient();
        await client.ConnectAsync(_config.Host, _config.FilePort, cancellationToken);
        using var stream = client.GetStream();

        // Header: [Cmd: 1B] [FileId: 16B] [FileSize: 8B] [IsImage: 1B] [NameLen: 2B] [FileName: UTF-8]
        stream.WriteByte(CommandUpload);

        byte[] guidBytes = fileId.ToByteArray();
        await stream.WriteAsync(guidBytes, cancellationToken);

        byte[] sizeBytes = BitConverter.GetBytes(fileSize);
        await stream.WriteAsync(sizeBytes, cancellationToken);

        stream.WriteByte((byte)(isImage ? 1 : 0));

        byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
        byte[] nameLenBytes = BitConverter.GetBytes((short)nameBytes.Length);
        await stream.WriteAsync(nameLenBytes, cancellationToken);
        await stream.WriteAsync(nameBytes, cancellationToken);

        // Stream file chunks in 64 KB buffer (Memory safe, async non-blocking)
        byte[] buffer = new byte[BufferSize];
        long bytesSent = 0;
        var uploadSw = System.Diagnostics.Stopwatch.StartNew();
        long lastUploadReport = 0;

        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        {
            int read;
            while ((read = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesSent += read;

                long elapsed = uploadSw.ElapsedMilliseconds;
                if (elapsed - lastUploadReport >= 50 || bytesSent == fileSize)
                {
                    lastUploadReport = elapsed;
                    progress?.Report((bytesSent, fileSize));
                }
            }
        }

        await stream.FlushAsync(cancellationToken);

        // Read 1-byte Server Ack
        int ack = stream.ReadByte();
        if (ack != 1)
        {
            throw new IOException("Server rejected or failed to process file upload.");
        }

        return new FileAttachmentDto
        {
            FileId = fileId,
            FileName = fileName,
            FileSize = fileSize,
            IsImage = isImage,
            ThumbnailBase64 = thumbnailBase64,
            LocalFilePath = filePath
        };
    }

    public async Task DownloadFileAsync(
        Guid fileId,
        string destinationPath,
        IProgress<(long received, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_config.Host, _config.FilePort, cancellationToken);
        using var stream = client.GetStream();

        // Send: [Cmd: 1B] [FileId: 16B]
        stream.WriteByte(CommandDownload);
        byte[] guidBytes = fileId.ToByteArray();
        await stream.WriteAsync(guidBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // Read FileSize (8 bytes)
        byte[]? sizeBuffer = await ReadExactAsync(stream, 8, cancellationToken);
        if (sizeBuffer == null) throw new IOException("Server closed connection during download.");

        long fileSize = BitConverter.ToInt64(sizeBuffer, 0);
        if (fileSize < 0)
        {
            throw new FileNotFoundException("Requested file does not exist on server.", fileId.ToString());
        }

        // Read FileName
        byte[]? nameLenBuffer = await ReadExactAsync(stream, 2, cancellationToken);
        if (nameLenBuffer == null) throw new IOException("Failed to read filename length.");
        short nameLen = BitConverter.ToInt16(nameLenBuffer, 0);

        byte[]? nameBuffer = await ReadExactAsync(stream, nameLen, cancellationToken);
        if (nameBuffer == null) throw new IOException("Failed to read filename.");

        // Stream file chunks to destination disk
        byte[] buffer = new byte[BufferSize];
        long remaining = fileSize;
        long bytesReceived = 0;
        var downloadSw = System.Diagnostics.Stopwatch.StartNew();
        long lastDownloadReport = 0;

        string tempDest = destinationPath + ".downloading";
        using (var fileStream = new FileStream(tempDest, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                if (read <= 0)
                {
                    throw new IOException("Server disconnected before download completed.");
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesReceived += read;
                remaining -= read;

                long elapsed = downloadSw.ElapsedMilliseconds;
                if (elapsed - lastDownloadReport >= 50 || remaining == 0)
                {
                    lastDownloadReport = elapsed;
                    progress?.Report((bytesReceived, fileSize));
                }
            }
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }
        File.Move(tempDest, destinationPath);
    }

    public static string? GenerateThumbnailBase64(string imagePath)
    {
        string? base64Result = null;

        // Run on a dedicated background STA thread to avoid blocking or freezing the main UI thread
        var thread = new Thread(() =>
        {
            try
            {
                using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = fileStream;
                bitmap.DecodePixelWidth = 160; // Max thumbnail width 160px for compact memory
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var memoryStream = new MemoryStream();
                encoder.Save(memoryStream);
                base64Result = Convert.ToBase64String(memoryStream.ToArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GENERATE THUMBNAIL ERROR] {ex.Message}");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        return base64Result;
    }

    private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read <= 0) return null;
            offset += read;
        }
        return buffer;
    }
}
