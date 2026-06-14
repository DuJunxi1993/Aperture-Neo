using SkiaSharp;

namespace ApertureNeo.Models;

/// <summary>
/// Result of a <see cref="Services.ImageLoader.LoadAsync"/> call.
/// Always non-null; on failure <see cref="Bitmap"/> is null and
/// <see cref="ErrorMessage"/> carries a human-readable reason
/// (decode error, cancellation, exception message, etc.).
/// </summary>
public class ImageLoadResult
{
    public string FilePath { get; init; } = "";
    public SKBitmap? Bitmap { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Construct a failure result with no bitmap. Caller is
    /// expected to dispose the <see cref="Bitmap"/> on the
    /// success path; failures carry no native resources.
    /// </summary>
    public static ImageLoadResult Failed(string path, string message) =>
        new() { FilePath = path, IsSuccess = false, ErrorMessage = message };
}