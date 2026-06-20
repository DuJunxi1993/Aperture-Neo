using System.Collections.Generic;

namespace ApertureNeo.Plugins.Ocr.Models;

public sealed class OcrResult
{
    public string FullText { get; init; } = string.Empty;

    public IReadOnlyList<OcrLine> Lines { get; init; } = new List<OcrLine>();

    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public double ElapsedMs { get; init; }
}
