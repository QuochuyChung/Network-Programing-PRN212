using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatClient.Services;
using ChatProtocol.Dtos;
using Microsoft.Win32;

namespace ChatClient.Views;

public partial class ImageViewerWindow : Window
{
    private readonly FileAttachmentDto _attachment;
    private string? _loadedFilePath;

    public ImageViewerWindow(FileAttachmentDto attachment, ImageSource? initialThumbnail)
    {
        InitializeComponent();
        _attachment = attachment;

        TxtFileName.Text = attachment.FileName;
        TxtFileSize.Text = attachment.FormattedFileSize;

        if (initialThumbnail != null)
        {
            MainImage.Source = initialThumbnail;
        }

        Loaded += async (s, e) => await LoadFullImageAsync();
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private async Task LoadFullImageAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(_attachment.LocalFilePath) && File.Exists(_attachment.LocalFilePath))
            {
                _loadedFilePath = _attachment.LocalFilePath;
                DisplayImageFromFile(_loadedFilePath);
                return;
            }

            // Download from File Server into local temp cache
            string cacheDir = Path.Combine(AppContext.BaseDirectory, "Cache", "images");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            string targetPath = Path.Combine(cacheDir, $"{_attachment.FileId}_{Path.GetFileName(_attachment.FileName)}");
            if (File.Exists(targetPath))
            {
                _loadedFilePath = targetPath;
                DisplayImageFromFile(targetPath);
                return;
            }

            LoadingBar.Visibility = Visibility.Visible;
            await FileTransferService.Instance.DownloadFileAsync(_attachment.FileId, targetPath);
            LoadingBar.Visibility = Visibility.Collapsed;

            _loadedFilePath = targetPath;
            DisplayImageFromFile(targetPath);
        }
        catch (Exception ex)
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            Console.WriteLine($"[IMAGE VIEWER ERROR] {ex.Message}");
        }
    }

    private void DisplayImageFromFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            MainImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DISPLAY IMAGE ERROR] {ex.Message}");
        }
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_loadedFilePath) || !File.Exists(_loadedFilePath))
        {
            MessageBox.Show("Image is still loading, please wait a moment.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string ext = Path.GetExtension(_attachment.FileName);
        var saveDialog = new SaveFileDialog
        {
            FileName = _attachment.FileName,
            Filter = $"Image File (*{ext})|*{ext}|All Files (*.*)|*.*"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                File.Copy(_loadedFilePath, saveDialog.FileName, overwrite: true);
                MessageBox.Show("Image saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
