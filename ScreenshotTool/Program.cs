using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
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
        Trace($"RunArea: ShowDialog returned {result}, IsFullscreen={overlay.IsFullscreen}, SelectedRegion={overlay.SelectedRegion}");

        if (result != true) return;

        if (overlay.IsFullscreen)
        {
            CaptureAndShowEditor(() => CaptureEngine.CaptureFullscreen());
        }
        else if (overlay.SelectedRegion.HasValue)
        {
            var region = overlay.SelectedRegion.Value;
            CaptureAndShowEditor(() => CaptureEngine.CaptureRegion(region));
        }
    }

    private static void CaptureAndShowEditor(Func<Bitmap> capture)
    {
        Trace("CaptureAndShowEditor: scheduling at ContextIdle");
        Bitmap? bitmap = null;
        Exception? error = null;

        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            Trace("CaptureAndShowEditor: ContextIdle action running");
            try { bitmap = capture(); }
            catch (Exception ex) { error = ex; Trace("CaptureAndShowEditor: capture threw " + ex.Message); }
        }), DispatcherPriority.ContextIdle);

        // Pump the dispatcher until the ContextIdle action completes.
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            Trace("CaptureAndShowEditor: post-action dispatcher message");
            frame.Continue = false;
        }), DispatcherPriority.Background);
        Trace("CaptureAndShowEditor: PushFrame (waiting for capture)");
        Dispatcher.PushFrame(frame);
        Trace($"CaptureAndShowEditor: frame done, bitmap={(bitmap != null ? "ok" : "null")}, error={(error != null ? error.GetType().Name : "none")}");

        if (error != null)
        {
            LogError("CaptureAndShowEditor.capture", error);
            MessageBox.Show($"截图失败: {error.Message}", "ScreenshotTool",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (bitmap != null)
        {
            try
            {
                Trace("CaptureAndShowEditor: creating EditorWindow");
                var editor = new EditorWindow(bitmap);
                Trace("CaptureAndShowEditor: editor.ShowDialog");
                editor.ShowDialog();
                Trace("CaptureAndShowEditor: editor closed");
            }
            finally
            {
                bitmap.Dispose();
            }
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
