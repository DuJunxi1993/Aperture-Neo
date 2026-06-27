using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ApertureNeo.Plugins.Screenshot;

public static class CaptureEngine
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    private const int SRCCOPY = 0xCC0020;

    public static Bitmap CaptureFullscreen()
    {
        var bounds = new System.Drawing.Rectangle(
            (int)SystemParameters.VirtualScreenLeft,
            (int)SystemParameters.VirtualScreenTop,
            (int)SystemParameters.VirtualScreenWidth,
            (int)SystemParameters.VirtualScreenHeight);
        return CaptureRegion(bounds);
    }

    public static Bitmap CaptureRegion(System.Drawing.Rectangle rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static BitmapSource BitmapToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    public static BitmapSource BitmapToBitmapSource(byte[] imageBytes)
    {
        using var ms = new MemoryStream(imageBytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    public static string SaveToTempPng(Bitmap bitmap, out string filePath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApertureNeo", "screenshots");
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, $"screenshot-{Guid.NewGuid():N}.png");
        bitmap.Save(filePath, ImageFormat.Png);
        return filePath;
    }
}

internal static class NativeMethods
{
    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr hObject);
}
