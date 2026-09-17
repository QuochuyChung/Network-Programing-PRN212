namespace ChatProtocol.Dtos;

public class FileAttachmentDto
{
    public Guid FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool IsImage { get; set; }
    public string? ThumbnailBase64 { get; set; }
    public string? LocalFilePath { get; set; }

    public string FormattedFileSize
    {
        get
        {
            if (FileSize < 1024)
                return $"{FileSize} B";
            if (FileSize < 1024 * 1024)
                return $"{(FileSize / 1024.0):F1} KB";
            if (FileSize < 1024 * 1024 * 1024)
                return $"{(FileSize / (1024.0 * 1024)):F1} MB";
            return $"{(FileSize / (1024.0 * 1024 * 1024)):F2} GB";
        }
    }
}
