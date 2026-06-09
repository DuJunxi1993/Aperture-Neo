using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ApertureNeo.Models;

public class ImageItem : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;
    private bool _hasThumbnailError;
    private string? _thumbnailErrorMessage;

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath).ToUpperInvariant();
    public long FileSize { get; }
    public DateTime LastWriteTime { get; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FileSizeKB => Math.Round(FileSize / 1024.0, 1);

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail == value) return;
            _thumbnail = value;
            if (value != null)
            {
                HasThumbnailError = false;
                ThumbnailErrorMessage = null;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public bool HasThumbnailError
    {
        get => _hasThumbnailError;
        set
        {
            if (_hasThumbnailError == value) return;
            _hasThumbnailError = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasThumbnailError)));
        }
    }

    public string? ThumbnailErrorMessage
    {
        get => _thumbnailErrorMessage;
        set
        {
            if (_thumbnailErrorMessage == value) return;
            _thumbnailErrorMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailErrorMessage)));
        }
    }

    public ImageItem(string filePath)
    {
        FilePath = filePath;
        var info = new FileInfo(filePath);
        FileSize = info.Length;
        LastWriteTime = info.LastWriteTime;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
