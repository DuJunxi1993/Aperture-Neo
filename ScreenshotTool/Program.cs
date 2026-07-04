using System;
using System.IO;
using System.Windows;
using ApertureNeo.Plugins.Screenshot;

namespace ScreenshotTool;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "--area";

        try
        {
            switch (mode)
            {
                case "--area":
                    RunArea();
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
        }
        catch (Exception ex)
        {
            LogError("Program.Main", ex);
            MessageBox.Show($"截图工具异常: {ex.Message}\n\n详情: %TEMP%\\ApertureNeo\\screenshot-error.log",
                "ScreenshotTool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            app.Shutdown();
        }

        return 0;
    }

    private static void RunArea()
    {
        Trace("RunArea: entered");
        var overlay = new RegionOverlay();
        var result = overlay.ShowDialog();
        Trace($"RunArea: ShowDialog returned {result}, Confirmed={(overlay.ConfirmedCapture != null ? "set" : "null")}, Full={(overlay.FullscreenCapture != null ? "set" : "null")}, SelectedRegion={overlay.SelectedRegion}");

        if (result != true)
        {
            overlay.FullscreenCapture?.Dispose();
            overlay.ConfirmedCapture?.Dispose();
            return;
        }

        if (overlay.FullscreenCapture != null)
        {
            try
            {
                Trace("RunArea: creating EditorWindow (fullscreen)");
                var editor = new EditorWindow(overlay.FullscreenCapture);
                Trace("RunArea: calling editor.ShowDialog (fullscreen)");
                editor.ShowDialog();
                Trace("RunArea: editor.ShowDialog returned (fullscreen)");
            }
            finally { overlay.FullscreenCapture.Dispose(); }
        }
        else if (overlay.ConfirmedCapture != null)
        {
            try
            {
                Trace("RunArea: creating EditorWindow (region)");
                var editor = new EditorWindow(overlay.ConfirmedCapture);
                Trace("RunArea: calling editor.ShowDialog (region)");
                editor.ShowDialog();
                Trace("RunArea: editor.ShowDialog returned (region)");
            }
            finally { overlay.ConfirmedCapture.Dispose(); }
        }
        else
        {
            LogError("RunArea.fallback", new InvalidOperationException(
                $"Dialog closed with no captured bitmap. SelectedRegion={overlay.SelectedRegion}"));
        }
    }

    private static void LogError(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ApertureNeo");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "screenshot-error.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {source}\n{ex}\n\n");
        }
        catch { }
    }

    private static void Trace(string message)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ApertureNeo");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "screenshot-trace.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\n");
        }
        catch { }
    }
}
