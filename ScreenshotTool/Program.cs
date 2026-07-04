using System;
using System.Windows;
using ApertureNeo.Plugins.Screenshot;

namespace ScreenshotTool;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };

        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "--area";

        switch (mode)
        {
            case "--area":
                var overlay = new RegionOverlay();
                if (overlay.ShowDialog() == true && overlay.SelectedRegion.HasValue)
                {
                    using var bitmap = CaptureEngine.CaptureRegion(overlay.SelectedRegion.Value);
                    new EditorWindow(bitmap).ShowDialog();
                }
                break;

            case "--fullscreen":
                using (var fsBitmap = CaptureEngine.CaptureFullscreen())
                    new EditorWindow(fsBitmap).ShowDialog();
                break;

            case "--daemon":
                MessageBox.Show("Daemon mode not implemented in MVP.", "ScreenshotTool",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                break;

            default:
                MessageBox.Show("Usage:\n  ScreenshotTool.exe --area\n  ScreenshotTool.exe --fullscreen",
                    "ScreenshotTool", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }

        return 0;
    }
}
