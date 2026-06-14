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
public class ImageLoader
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

    /// <summary>
    /// Decode <paramref name="path"/> to an <see cref="ImageLoadResult"/>
    /// on a worker thread. Honors the cancellation token at
    /// pre-decode (after FileInfo) and at post-decode (after codec
    /// open) checkpoints. Never throws — failure modes are returned
    /// in <see cref="ImageLoadResult.ErrorMessage"/>.
    /// </summary>
    public Task<ImageLoadResult> LoadAsync(string path, CancellationToken ct = default)
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
                var maxDim = Math.Max(info.Width, info.Height);
                var scale = maxDim > _maxDecodeDimension ? (float)_maxDecodeDimension / maxDim : 1f;
                var decodeW = Math.Max(1, (int)(info.Width * scale));
                var decodeH = Math.Max(1, (int)(info.Height * scale));

                ct.ThrowIfCancellationRequested();
                var bitmap = SKBitmap.Decode(codec, new SKImageInfo(decodeW, decodeH, SKColorType.Rgba8888));
                if (bitmap == null)
                    return ImageLoadResult.Failed(path, "解码失败");

                return new ImageLoadResult
                {
                    FilePath = path,
                    Bitmap = bitmap,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
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