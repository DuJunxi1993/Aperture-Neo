using System;
using System.Windows;
using ApertureNeo.Plugins.Screenshot;

namespace ScreenshotTool;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application();

        string? mode = args.Length > 0 ? args[0].ToLowerInvariant() : null;

        if (mode == "--area" || mode == null)
        {
            // Show region selection overlay
            var overlay = new RegionOverlay();
            bool? result = overlay.ShowDialog();

            if (result == true && overlay.SelectedRegion.HasValue)
            {
                using var bitmap = CaptureEngine.CaptureRegion(overlay.SelectedRegion.Value);
                var editor = new EditorWindow(bitmap);
                editor.ShowDialog();
            }
        }
        else if (mode == "--fullscreen")
        {
            using var bitmap = CaptureEngine.CaptureFullscreen();
            var editor = new EditorWindow(bitmap);
            editor.ShowDialog();
        }
        else if (mode == "--daemon")
        {
            MessageBox.Show("Daemon mode not implemented in MVP. Use --area or --fullscreen.",
                "ScreenshotTool MVP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                "Usage:\n  ScreenshotTool.exe --area       Area selection\n  ScreenshotTool.exe --fullscreen Fullscreen capture",
                "ScreenshotTool MVP", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        app.Shutdown();
    }
}
