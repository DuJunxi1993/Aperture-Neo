using SkiaSharp;

namespace ApertureNeo.Models;

public class ImageLoadResult
{
    public string FilePath { get; init; } = "";
    public SKBitmap? Bitmap { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }

    public static ImageLoadResult Failed(string path, string message) =>
        new() { FilePath = path, IsSuccess = false, ErrorMessage = message };
}