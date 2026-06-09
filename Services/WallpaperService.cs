using System;
using System.Runtime.InteropServices;

namespace ApertureNeo.Services;

public static class WallpaperService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

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
