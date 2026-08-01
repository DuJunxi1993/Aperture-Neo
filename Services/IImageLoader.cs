using System.Threading;
using System.Threading.Tasks;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Image decoder. Reads a file and returns a decoded SKBitmap at
/// a caller-specified target resolution. Implementations may use
/// WIC (hardware-accelerated) or SkiaSharp (CPU) as the backend.
/// </summary>
public interface IImageLoader
{
    /// <summary>
    /// Upper bound on the longest edge of the decoded bitmap.
    /// Sources larger than this are downsampled during decode.
    /// </summary>
    int MaxDecodeDimension { get; set; }

    /// <summary>
    /// Decode <paramref name="path"/> at the source's native
    /// resolution (no downscaling).
    /// </summary>
    Task<ImageLoadResult> LoadAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Decode <paramref name="path"/> at a target resolution.
    /// The implementation downscales the source to fit within
    /// <c>targetW</c> × <c>targetH</c> while preserving the
    /// aspect ratio. Pass 0 for either dimension to mean
    /// "derive from the other dimension" (at least one must be
    /// non-zero for downscaling to work; if both are 0 the
    /// source resolution is used).
    /// </summary>
    Task<ImageLoadResult> LoadAsync(string path, int targetW, int targetH, CancellationToken ct = default);
}