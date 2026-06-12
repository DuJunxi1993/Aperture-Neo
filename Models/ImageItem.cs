using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SkiaSharp;
namespace ApertureNeo.Models;

public class ImageItem : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;
    private bool _hasThumbnailError;
    private string? _thumbnailErrorMessage;
    private long? _fileSize;
    private DateTime? _lastWriteTime;
    private int? _width;
    private int? _height;
    private bool _dimensionsProbing;

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

    /// <summary>
    /// Image width in pixels. SkiaCodec reads just the header (no decode),
    /// so probing is cheap (~1ms for the header read). Triggered on
    /// first access; subsequent reads are cached. Notifications fire when
    /// the probe completes, so bound UI updates from null to the real
    /// value automatically.
    /// </summary>
    public int? Width
    {
        get
        {
            EnsureDimensionsProbed();
            return _width;
        }
    }

    /// <summary>Image height in pixels (see Width for probing semantics).</summary>
    public int? Height
    {
        get
        {
            EnsureDimensionsProbed();
            return _height;
        }
    }

    private void EnsureDimensionsProbed()
    {
        if (_width.HasValue || _dimensionsProbing) return;
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            _dimensionsProbing = true; // mark done to avoid retries
            return;
        }
        _dimensionsProbing = true;
        // Fire-and-forget probe; writes _width/_height on completion and
        // raises PropertyChanged so bound UI updates automatically.
        _ = Task.Run(() =>
        {
            try
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                using var codec = SKCodec.Create(stream);
                if (codec != null)
                {
                    SetDimensions(codec.Info.Width, codec.Info.Height);
                }
            }
            catch { /* leave as null on failure */ }
        });
    }

    /// <summary>
    /// Update width/height. Public so the image loader can write the
    /// authoritative decoded dimensions (bypassing the header-only probe)
    /// once the full bitmap is available.
    /// </summary>
    public void SetDimensions(int width, int height)
    {
        var widthChanged = _width != width;
        var heightChanged = _height != height;
        _width = width;
        _height = height;
        if (widthChanged) OnPropertyChanged(nameof(Width));
        if (heightChanged) OnPropertyChanged(nameof(Height));
        if (widthChanged || heightChanged) OnPropertyChanged(nameof(AspectRatio));
    }

    public double FileSizeKB => Math.Round(FileSize / 1024.0, 1);

    /// <summary>
    /// Pixel width / pixel height. Returns 1.0 (square fallback) while
    /// the dimensions are still being probed on first access, so the
    /// thumbnail grid never collapses to a 0-height row before the
    /// probe completes. Bindings on this property are notified both on
    /// the initial null→1.0 transition and on the final real-value
    /// update — see <see cref="SetDimensions"/>.
    /// </summary>
    public double AspectRatio
    {
        get
        {
            EnsureDimensionsProbed();
            if (_width == null || _height == null || _height == 0) return 1.0;
            return (double)_width.Value / _height.Value;
        }
    }

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
            OnPropertyChanged(nameof(Thumbnail));
        }
    }

    public bool HasThumbnailError
    {
        get => _hasThumbnailError;
        set
        {
            if (_hasThumbnailError == value) return;
            _hasThumbnailError = value;
            OnPropertyChanged(nameof(HasThumbnailError));
        }
    }

    public string? ThumbnailErrorMessage
    {
        get => _thumbnailErrorMessage;
        set
        {
            if (_thumbnailErrorMessage == value) return;
            _thumbnailErrorMessage = value;
            OnPropertyChanged(nameof(ThumbnailErrorMessage));
        }
    }

    /// <summary>
    /// Construct without touching the filesystem. FileSize/LastWriteTime
    /// and Width/Height are resolved on first access (or via
    /// SetDimensions when the loader provides authoritative values).
    /// This is critical for folders with many items where ImageItem
    /// construction is on the hot path.
    /// </summary>
    public ImageItem(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// EXIF metadata (Make/Model/DateTaken). Round 69: lazy-loaded via
    /// WPF <see cref="BitmapMetadata"/> which reads the JPEG APP1
    /// segment without decoding pixels (~1-2 ms). Returns null for
    /// non-JPEG sources (PNG/WebP/HEIC) or when the file has no EXIF.
    /// Use <see cref="GetExifValue"/> to query a specific tag, e.g.
    /// <c>GetExifValue("/app1/ifd/{ushort=271}")</c> for Make.
    /// </summary>
    public BitmapMetadata? Exif => EnsureExifLoaded();

    private BitmapMetadata? _exif;
    private bool _exifProbing;
    private BitmapMetadata? EnsureExifLoaded()
    {
        if (_exif != null) return _exif;
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath)) return null;
        if (_exifProbing) return null;
        _exifProbing = true;
        // Fire-and-forget; same pattern as EnsureDimensionsProbed.
        // BitmapFrame.Create with DelayCreation only parses the header
        // — no pixel decode.
        _ = Task.Run(() =>
        {
            try
            {
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (frame.Metadata is BitmapMetadata md)
                {
                    _exif = md;
                }
            }
            catch { /* leave null on failure */ }
        });
        return null;
    }

    /// <summary>
    /// Round 69: read a single EXIF tag from <see cref="Exif"/>.
    /// Returns null if the metadata isn't loaded yet, the query
    /// string is malformed, or the tag isn't present in the file.
    /// Common IFD0 IDs: 271 Make, 272 Model, 306 DateTime,
    /// 33434 ExposureTime, 33437 FNumber, 34855 ISOSpeedRatings.
    /// Path "/app1/ifd/{ushort=N}" addresses the IFD0 tags directly.
    /// </summary>
    public string? GetExifValue(string query)
    {
        if (_exif == null) return null;
        try
        {
            if (!_exif.ContainsQuery(query)) return null;
            var raw = _exif.GetQuery(query);
            return raw switch
            {
                string s when !string.IsNullOrWhiteSpace(s) => s.Trim(),
                _ => null
            };
        }
        catch { return null; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}