namespace ApertureNeo.Helpers;

/// <summary>
/// Formatting helpers for image-related values shown in the UI.
/// </summary>
public static class ImageFormatHelper
{
    /// <summary>
    /// Format a byte count as a human-readable file size (B / KB / MB / GB).
    /// Uses one decimal place for fractional units, no decimals for bytes.
    /// </summary>
    /// <param name="bytes">Byte count to format. Negative values render as
    /// their absolute byte count (e.g. "-1024" → "-1024 B").</param>
    /// <returns>Size string in the largest unit that produces a value &gt;= 1.</returns>
    public static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
