using System.Threading;
using System.Threading.Tasks;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// SkiaSharp-based image decoder. Reads the file off-thread, asks
/// the codec for the source dimensions, and if the long edge
/// exceeds <see cref="MaxDecodeDimension"/> allocates a
/// pre-scaled bitmap to keep allocations bounded for huge photos.
/// </summary>
public interface IImageLoader
{
    /// <summary>
    /// Upper bound on the longest edge of the decoded bitmap.
    /// Sources larger than this are downsampled during decode
    /// (preserving aspect ratio). Clamped to [1080, 7680].
    /// </summary>
    int MaxDecodeDimension { get; set; }

    /// <summary>Decode <paramref name="path"/> and return the resulting <see cref="ImageLoadResult"/>.</summary>
    Task<ImageLoadResult> LoadAsync(string path, CancellationToken ct = default);
}