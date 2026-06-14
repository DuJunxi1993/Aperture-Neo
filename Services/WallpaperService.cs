using System;
using System.Runtime.InteropServices;

namespace ApertureNeo.Services;

/// <summary>
/// Thin wrapper over Win32 <c>SystemParametersInfo</c> with
/// <c>SPI_SETDESKWALLPAPER</c> to set the current image as the
/// Windows desktop wallpaper. No-op (returns false) on any
/// failure — the right-click context menu can survive the
/// wallpaper API being denied without breaking the app.
/// </summary>
public static class WallpaperService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    /// <summary>
    /// Set <paramref name="imagePath"/> as the desktop wallpaper.
    /// Returns true on success, false on any failure (empty path,
    /// API denied, etc.). The wallpaper registry entry is updated
    /// and the change is broadcast to the shell.
    /// </summary>
    public static bool TrySetDesktop(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return false;
        try
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
