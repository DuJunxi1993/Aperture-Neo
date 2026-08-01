using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ApertureNeo.Models;
using SkiaSharp;

namespace ApertureNeo.Services;

/// <summary>
/// SkiaSharp-based image decoder. Reads the file off-thread, asks
/// the codec for the source dimensions, and if the long edge
/// exceeds <see cref="MaxDecodeDimension"/> allocates a
/// pre-scaled bitmap of <c>(decodeW, decodeH)</c> to keep
/// allocations bounded for huge photos (8K JPEGs etc.).
/// </summary>
public class ImageLoader : IImageLoader
{
    private int _maxDecodeDimension = 7680;

    /// <summary>
    /// Upper bound on the longest edge of the decoded bitmap, in
    /// pixels. Sources larger than this are downsampled during
    /// decode (preserving aspect ratio). Clamped to [1080, 7680].
    /// Default 7680 covers 8K sources.
    /// </summary>
    public int MaxDecodeDimension
    {
        get => _maxDecodeDimension;
        set => _maxDecodeDimension = Math.Clamp(value, 1080, 7680);
    }

    public Task<ImageLoadResult> LoadAsync(string path, CancellationToken ct = default)
        => LoadAsync(path, 0, 0, ct);

    public Task<ImageLoadResult> LoadAsync(string path, int targetW, int targetH, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
                using var codec = SKCodec.Create(stream);
                if (codec == null)
                    return ImageLoadResult.Failed(path, "无法解码");

                var info = codec.Info;
                int srcW = info.Width;
                int srcH = info.Height;

                // Clamp decode dimensions. When targetW/targetH are 0,
                // the existing MaxDecodeDimension cap is used instead.
                if (targetW <= 0) targetW = srcW;
                if (targetH <= 0) targetH = srcH;

                var scale = 1f;
                scale = Math.Min(scale, (float)targetW / srcW);
                scale = Math.Min(scale, (float)targetH / srcH);
                scale = Math.Min(scale, (float)_maxDecodeDimension / Math.Max(srcW, srcH));
                scale = Math.Clamp(scale, 0.01f, 1f);

                // Snap to a codec-supported size: libjpeg-turbo only
                // scales JPEGs by fixed DCT factors (1/8..7/8), so
                // arbitrary dimensions make GetPixels fail
                // (InvalidScale). GetScaledDimensions returns the
                // closest supported size for the codec.
                var scaled = codec.GetScaledDimensions(scale);

                ct.ThrowIfCancellationRequested();
                var bitmap = SKBitmap.Decode(codec, new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888));
                if (bitmap == null)
                {
                    // Codec can't scale (rare) — full-res decode,
                    // same as pre-adaptive behavior.
                    bitmap = SKBitmap.Decode(codec);
                }
                if (bitmap == null)
                    return ImageLoadResult.Failed(path, "解码失败");

                return new ImageLoadResult
                {
                    FilePath = path,
                    Bitmap = bitmap,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    SourceWidth = srcW,
                    SourceHeight = srcH,
                    IsSuccess = true
                };
            }
            catch (OperationCanceledException)
            {
                return ImageLoadResult.Failed(path, "已取消");
            }
            catch (Exception ex)
            {
                return ImageLoadResult.Failed(path, ex.Message);
            }
        }, ct);
    }
}