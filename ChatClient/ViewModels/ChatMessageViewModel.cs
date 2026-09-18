using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatClient.Services;
using ChatClient.Utils;
using ChatProtocol.Dtos;
using Microsoft.Win32;

namespace ChatClient.ViewModels;

public class ChatMessageViewModel : ViewModelBase
{
    private string _content = string.Empty;
    private bool _isUploading;
    private double _uploadProgress;
    private string _progressText = string.Empty;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _downloadText = string.Empty;
    private ImageSource? _thumbnailSource;

    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Sender { get; set; } = string.Empty;

    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
            {
                ParseAttachmentIfAny();
            }
        }
    }

    public DateTime Timestamp { get; set; }

    public bool IsMine { get; set; }
    public bool IsSystem => string.Equals(Sender, "system", StringComparison.OrdinalIgnoreCase);

    public bool IsImage { get; set; }
    public bool IsFile { get; set; }
    public bool IsTextMessage => !IsImage && !IsFile;
    public FileAttachmentDto? Attachment { get; set; }

    public ImageSource? ThumbnailSource
    {
        get => _thumbnailSource;
        set => SetProperty(ref _thumbnailSource, value);
    }

    public bool IsUploading
    {
        get => _isUploading;
        set
        {
            if (SetProperty(ref _isUploading, value))
            {
                OnPropertyChanged(nameof(ShowUploadProgress));
            }
        }
    }

    public double UploadProgress
    {
        get => _uploadProgress;
        set => SetProperty(ref _uploadProgress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnPropertyChanged(nameof(ShowDownloadProgress));
                OnPropertyChanged(nameof(ShowDownloadButton));
            }
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    public string DownloadText
    {
        get => _downloadText;
        set => SetProperty(ref _downloadText, value);
    }

    public bool ShowUploadProgress => IsUploading;
    public bool ShowDownloadProgress => IsDownloading;
    public bool ShowDownloadButton => IsFile && !IsUploading && !IsDownloading;

    public CancellationTokenSource? UploadCts { get; set; }

    public ICommand? CancelUploadCommand { get; set; }
    public ICommand? DownloadFileCommand { get; set; }
    public ICommand? PreviewImageCommand { get; set; }

    public event Action<FileAttachmentDto, ImageSource?>? PreviewRequested;

    public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm");

    public string AvatarLetter => string.IsNullOrWhiteSpace(Sender) ? "?" : char.ToUpper(Sender.Trim()[0]).ToString();
    public string AvatarColor => AvatarColorGenerator.GetColorForName(Sender);

    public HorizontalAlignment BubbleAlignment =>
        IsSystem ? HorizontalAlignment.Center : (IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left);

    public string BubbleBackground =>
        IsSystem ? "#E2E8F0" : (IsMine ? "#0084FF" : "#F1F5F9");

    public string BubbleForeground =>
        IsSystem ? "#475569" : (IsMine ? "#FFFFFF" : "#0F172A");

    public Visibility AvatarVisibility =>
        (IsMine || IsSystem) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SenderNameVisibility =>
        (IsMine || IsSystem) ? Visibility.Collapsed : Visibility.Visible;

    public ChatMessageViewModel()
    {
        CancelUploadCommand = new RelayCommand(_ =>
        {
            UploadCts?.Cancel();
            IsUploading = false;
            ProgressText = "Upload cancelled.";
        });

        DownloadFileCommand = new RelayCommand(async _ => await ExecuteDownloadAsync());
        PreviewImageCommand = new RelayCommand(_ =>
        {
            if (Attachment != null && Attachment.IsImage)
            {
                PreviewRequested?.Invoke(Attachment, ThumbnailSource);
            }
        });
    }

    private void ParseAttachmentIfAny()
    {
        if (string.IsNullOrEmpty(_content)) return;

        if (_content.StartsWith("[ATTACHMENT]:", StringComparison.Ordinal))
        {
            try
            {
                string json = _content.Substring("[ATTACHMENT]:".Length);
                var dto = JsonSerializer.Deserialize<FileAttachmentDto>(json);
                if (dto != null)
                {
                    Attachment = dto;
                    IsImage = dto.IsImage;
                    IsFile = !dto.IsImage;

                    OnPropertyChanged(nameof(IsImage));
                    OnPropertyChanged(nameof(IsFile));
                    OnPropertyChanged(nameof(IsTextMessage));
                    OnPropertyChanged(nameof(Attachment));
                    OnPropertyChanged(nameof(ShowDownloadButton));

                    if (IsImage)
                    {
                        LoadThumbnail(dto);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PARSE ATTACHMENT ERROR] {ex.Message}");
            }
        }
    }

    public void LoadThumbnail(FileAttachmentDto dto)
    {
        Task.Run(() =>
        {
            try
            {
                // 1. If local file exists, load via FileStream with FileShare.Read on background thread
                if (!string.IsNullOrEmpty(dto.LocalFilePath) && File.Exists(dto.LocalFilePath))
                {
                    using var fileStream = new FileStream(dto.LocalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = fileStream;
                    bmp.DecodePixelWidth = 320; // Bound to max 320px for memory efficiency
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.None;
                    bmp.EndInit();
                    bmp.Freeze(); // Frozen bitmaps can cross thread boundaries safely

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        ThumbnailSource = bmp;
                    });
                    return;
                }

                // 2. Otherwise, decode thumbnail Base64 on background thread
                if (!string.IsNullOrEmpty(dto.ThumbnailBase64))
                {
                    byte[] bytes = Convert.FromBase64String(dto.ThumbnailBase64);
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = ms;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.None;
                    bmp.EndInit();
                    bmp.Freeze(); // Frozen bitmaps can cross thread boundaries safely

                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        ThumbnailSource = bmp;
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOAD THUMBNAIL ERROR] {ex.Message}");
            }
        });
    }

    private async Task ExecuteDownloadAsync()
    {
        if (Attachment == null) return;

        var saveDialog = new SaveFileDialog
        {
            FileName = Attachment.FileName,
            Filter = "All Files (*.*)|*.*",
            Title = $"Save {Attachment.FileName}"
        };

        if (saveDialog.ShowDialog() != true) return;

        string destPath = saveDialog.FileName;
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadText = "0%";

        var progress = new Progress<(long received, long total)>(p =>
        {
            double pct = p.total > 0 ? (double)p.received / p.total * 100.0 : 0;
            DownloadProgress = pct;
            DownloadText = $"{pct:F0}% ({(p.received / 1024.0 / 1024.0):F1} MB / {(p.total / 1024.0 / 1024.0):F1} MB)";
        });

        try
        {
            await Task.Run(async () =>
            {
                await FileTransferService.Instance.DownloadFileAsync(Attachment.FileId, destPath, progress);
            });

            IsDownloading = false;
            DownloadText = "Downloaded";
            MessageBox.Show($"File saved successfully to:\n{destPath}", "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            DownloadText = "Failed";
            MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
