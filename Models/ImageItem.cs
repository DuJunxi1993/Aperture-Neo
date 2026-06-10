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
    private long? _fileSize;
    private DateTime? _lastWriteTime;

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath).ToUpperInvariant();

    /// <summary>
    /// File size in bytes. Lazily loaded from FileInfo on first access.
    /// Constructing an ImageItem no longer touches the filesystem, so
    /// opening a folder with thousands of images doesn't pay N×FileInfo
    /// round-trips up front.
    /// </summary>
    public long FileSize
    {
        get
        {
            if (!_fileSize.HasValue)
            {
                try { _fileSize = new FileInfo(FilePath).Length; }
                catch { _fileSize = 0; }
            }
            return _fileSize.Value;
        }
    }

    public DateTime LastWriteTime
    {
        get
        {
            if (!_lastWriteTime.HasValue)
            {
                try { _lastWriteTime = new FileInfo(FilePath).LastWriteTime; }
                catch { _lastWriteTime = DateTime.MinValue; }
            }
            return _lastWriteTime.Value;
        }
    }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public double FileSizeKB => Math.Round(FileSize / 1024.0, 1);

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

    /// <summary>
    /// Construct without touching the filesystem. FileSize/LastWriteTime are
    /// resolved on first access; this is critical for folders with many items
    /// where ImageItem construction is on the hot path.
    /// </summary>
    public ImageItem(string filePath)
    {
        FilePath = filePath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
