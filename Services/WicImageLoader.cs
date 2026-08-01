using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ApertureNeo.Models;
using SkiaSharp;

namespace ApertureNeo.Services;

/// <summary>
/// WIC-based image decoder. Uses WPF <c>BitmapDecoder</c> +
/// <c>BitmapImage</c> (which wraps the Windows Imaging Component)
/// to decode JPEG / PNG / TIFF / BMP / GIF / HEIF at a caller-
/// specified target resolution on a dedicated STA thread. WIC can
/// use DirectX hardware acceleration (DXVA) on supported GPUs,
/// which is significantly faster than CPU-only decode via SkiaSharp
/// for large images. Falls back to SkiaSharp's <see cref="ImageLoader"/>
/// when WIC does not support a format (e.g. WebP on older Windows).
/// </summary>
public sealed class WicImageLoader : IImageLoader, IDisposable
{
    private readonly SingleStaThread _staThread = new();
    private IImageLoader? _fallback;

    public int MaxDecodeDimension
    {
        get => 3840;
        set { }
    }

    public Task<ImageLoadResult> LoadAsync(string path, CancellationToken ct = default)
        => LoadAsync(path, 0, 0, ct);

    public async Task<ImageLoadResult> LoadAsync(string path, int targetW, int targetH, CancellationToken ct = default)
    {
        try
        {
            // Read file bytes on the ThreadPool so the UI thread
            // and the STA decode thread are never blocked by I/O.
            byte[] fileBytes = await Task.Run(() => File.ReadAllBytes(path), ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
                return ImageLoadResult.Failed(path, "已取消");

            // Decode via WIC on the dedicated STA thread.
            // The UI thread is not blocked — the STA thread runs
            // its own Dispatcher loop.
            var result = await _staThread.RunAsync(() =>
            {
                ct.ThrowIfCancellationRequested();

                // Work on locals — the captured targetW/targetH
                // are reused by the fallback if WIC rejects the
                // format, so they must not be mutated here.
                int tW = targetW;
                int tH = targetH;

                using var ms = new MemoryStream(fileBytes);
                int srcW, srcH;
                {
                    var decoder = BitmapDecoder.Create(ms,
                        BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
                    srcW = frame.PixelWidth;
                    srcH = frame.PixelHeight;
                }

                // Compute decode dimensions — only the size the
                // viewer actually needs, not the source resolution.
                if (tW <= 0) tW = srcW;
                if (tH <= 0) tH = (int)(tW * (long)srcH / srcW);

                int decodeW = Math.Min(tW, srcW);
                int decodeH = (int)(decodeW * (long)srcH / srcW);
                if (decodeH > Math.Min(tH, srcH))
                {
                    decodeH = Math.Min(tH, srcH);
                    decodeW = (int)(decodeH * (long)srcW / srcH);
                }
                if (decodeW < 4) decodeW = 4;
                if (decodeH < 4) decodeH = 4;

                ct.ThrowIfCancellationRequested();

                // Reuse the same stream for the decode (the probe
                // decoder is disposed, so it no longer holds the
                // stream). Saves one full byte[] copy per decode.
                ms.Position = 0;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.DecodePixelWidth = decodeW;
                bmp.DecodePixelHeight = decodeH;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                // Copy decoded pixels into a managed buffer
                int stride = decodeW * 4;
                var pixels = new byte[stride * decodeH];
                bmp.CopyPixels(pixels, stride, 0);

                // Transfer to SKBitmap for the SkiaSharp renderer
                var skBitmap = new SKBitmap(decodeW, decodeH, SKColorType.Bgra8888, SKAlphaType.Premul);
                Marshal.Copy(pixels, 0, skBitmap.GetPixels(), pixels.Length);

                return new ImageLoadResult
                {
                    FilePath = path,
                    Bitmap = skBitmap,
                    Width = decodeW,
                    Height = decodeH,
                    SourceWidth = srcW,
                    SourceHeight = srcH,
                    IsSuccess = true
                };
            }, ct).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException)
        {
            return ImageLoadResult.Failed(path, "已取消");
        }
        catch (NotSupportedException)
        {
            // WIC doesn't support this format (WebP, AVIF without
            // extension, etc.) — fall back to SkiaSharp CPU decode.
            // Forward the adaptive target size so the fallback
            // doesn't decode an 8K WebP at full resolution.
            return await FallbackAsync(path, targetW, targetH, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ImageLoadResult.Failed(path, ex.Message);
        }
    }

    private async Task<ImageLoadResult> FallbackAsync(string path, int targetW, int targetH, CancellationToken ct)
    {
        _fallback ??= new ImageLoader();
        return await _fallback.LoadAsync(path, targetW, targetH, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _staThread.Dispose();
    }
}
