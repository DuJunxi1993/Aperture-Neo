namespace ApertureNeo.Plugins.Ocr.Core.Models;

public sealed class OcrLine
{
    public string Text { get; init; } = string.Empty;

    public float Confidence { get; init; }
}
