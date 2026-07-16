using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ApertureNeo.Plugins.Ocr.Core.Models;
using ApertureNeo.Plugins.Ocr.Core.Services;
using ApertureNeo.Plugins.Ocr.Ui;
using ApertureNeo.Views;
using ApertureNeo.Services;
using SQLitePCL;

namespace ScreenshotTool;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Defensive: the main app initializes SQLitePCLRaw via
        // App.OnStartup before any SqliteConnection is opened
        // (ThumbnailCache is the only caller in this codebase).
        // ScreenshotTool.exe doesn't go through App.OnStartup,
        // so initialize Batteries_V2 here too. Idempotent, safe
        // to call even if a SQLite path is never hit.
        Batteries_V2.Init();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // The EditorWindow's XAML merges /ApertureNeo;component/
        // styles via the EditorWindow's own Window.Resources. Those
        // resources live in ApertureNeo's own assembly — the
        // /ApertureNeo;component/ pack URI should resolve to the
        // local ApertureNeo.dll. In some pack-URI-resolution
        // failure modes (e.g. the resource assembly isn't found
        // at the right probing path), the lookups silently fall
        // through to UnsetValue and the Border's Background=
        // "{StaticResource SurfaceCanvas}" throws XamlParseException.
        //
        // Belt-and-braces fix: also merge the dictionaries into
        // Application.Resources so the lookup finds them via
        // either path (Window scope or App scope). Redundant
        // merges are cheap — WPF's resource store de-dupes by
        // URI.
        app.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri("/ApertureNeo;component/DesignTokens.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri("/ApertureNeo;component/Styles/Annotation.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri("/ApertureNeo;component/Styles/Buttons.xaml", UriKind.Relative) });

        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "--area";

        try
        {
            switch (mode)
            {
                case "--area":
                    RunAreaAsync().GetAwaiter().GetResult();
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

    private static async Task RunAreaAsync()
    {
        Trace("RunArea: entered");
        var overlay = new RegionOverlay();
        var result = overlay.ShowDialog();
        Trace($"RunArea: ShowDialog returned {result}, Confirmed={overlay.Confirmed}, IsFullscreen={overlay.IsFullscreen}, SelectedRegion={overlay.SelectedRegion}, OcrRequested={overlay.OcrRequested}");

        // Use the explicit Confirmed flag instead of the DialogResult
        // return value — Confirmed is set in every click handler so
        // it works identically for Show() and ShowDialog() callers.
        if (!overlay.Confirmed) return;

        // Resolve the capture delegate once — used by either the
        // editor flow or the OCR flow. The OCR button reuses the
        // same selection logic as Confirm: prefer the dragged
        // region when present, fall back to fullscreen when nothing
        // was selected.
        Func<Bitmap>? capture = null;
        if (overlay.IsFullscreen)
        {
            capture = CaptureEngine.CaptureFullscreen;
        }
        else if (overlay.SelectedRegion.HasValue)
        {
            var region = overlay.SelectedRegion.Value;
            capture = () => CaptureEngine.CaptureRegion(region);
        }
        if (capture == null) return;

        if (overlay.OcrRequested)
        {
            // Read the same settings.json the main app writes to,
            // so the OCR behavior (clipboard vs result window)
            // matches the editor's OCR button exactly.
            var settings = new SettingsStore();
            await CaptureAndRunOcr(capture, settings).ConfigureAwait(true);
        }
        else
        {
            CaptureAndShowEditor(capture);
        }
    }

    private static void CaptureAndShowEditor(Func<Bitmap> capture)
    {
        Trace("CaptureAndShowEditor: scheduling capture at ContextIdle");
        var frame = new DispatcherFrame();
        Bitmap? bitmap = null;
        Exception? error = null;

        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            Trace("CaptureAndShowEditor: capture action running");
            try { bitmap = capture(); }
            catch (Exception ex) { error = ex; Trace("CaptureAndShowEditor: capture threw " + ex.Message); }
            frame.Continue = false;
        }), DispatcherPriority.ContextIdle);

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
                // Share settings with the main app: the same
                // settings.json under %APPDATA% holds the user's
                // preferred default save folder. Constructing
                // SettingsStore directly works because it's a
                // public, no-DI class — the standalone tool isn't
                // wired into AppHost's service container.
                var settings = new SettingsStore();
                var editor = new EditorWindow(bitmap, settings);
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

    /// <summary>
    /// Capture the region/fullscreen, save it to a temp PNG, run
    /// OCR via the shared <see cref="OcrService"/>, then dispatch
    /// the result the same way the editor's OCR button does:
    /// always copy the text to the clipboard, and additionally
    /// show <see cref="OcrResultWindow"/> only when the user has
    /// opted in via SettingsStore.EditorOcrShowWindow.
    /// </summary>
    private static async Task CaptureAndRunOcr(Func<Bitmap> capture, ISettingsStore settings)
    {
        Trace("CaptureAndRunOcr: scheduling capture at ContextIdle");
        var frame = new DispatcherFrame();
        Bitmap? bitmap = null;
        Exception? captureError = null;

        // CS4014 in async context: BeginInvoke returns DispatcherOperation
// (Task-like), but awaiting it would block until the action is
// queued, not until it runs. The actual synchronization point is
// PushFrame below — it blocks until the action sets frame.Continue.
// Fire-and-forget is intentional here.
#pragma warning disable CS4014
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            Trace("CaptureAndRunOcr: capture action running");
            try { bitmap = capture(); }
            catch (Exception ex) { captureError = ex; Trace("CaptureAndRunOcr: capture threw " + ex.Message); }
            frame.Continue = false;
        }), DispatcherPriority.ContextIdle);
#pragma warning restore CS4014

        Dispatcher.PushFrame(frame);

        if (captureError != null)
        {
            LogError("CaptureAndRunOcr.capture", captureError);
            MessageBox.Show($"截图失败: {captureError.Message}", "ScreenshotTool",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (bitmap == null) return;

        var tempPath = Path.Combine(
            Path.GetTempPath(), "ApertureNeo", "screenshots",
            $"screenshot-ocr-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        try
        {
            Trace($"CaptureAndRunOcr: saving bitmap to {tempPath}");
            bitmap.Save(tempPath, ImageFormat.Png);

            Trace("CaptureAndRunOcr: invoking OcrService.ExtractAsync");
            var ocr = new OcrService();
            OcrResult result = await ocr.ExtractAsync(tempPath).ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                Trace($"CaptureAndRunOcr: OCR failed: {result.ErrorMessage}");
                MessageBox.Show($"OCR 失败: {result.ErrorMessage}", "ScreenshotTool",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Trace($"CaptureAndRunOcr: OCR done, {result.Lines.Count} lines");

            // Match the editor: always copy to clipboard first.
            try { Clipboard.SetText(result.FullText); }
            catch (Exception ex)
            {
                // Clipboard.SetText throws COMException when another
                // process holds the clipboard. The OCR itself still
                // succeeded — surface the failure but continue so
                // the user can still see the result window if they
                // opted in.
                Trace($"CaptureAndRunOcr: clipboard write failed: {ex.Message}");
            }

            if (settings.EditorOcrShowWindow)
            {
                Trace("CaptureAndRunOcr: opening OcrResultWindow (EditorOcrShowWindow=true)");
                var win = new OcrResultWindow();
                win.SetResult(result, Path.GetFileName(tempPath));
                win.ShowDialog();
            }
            else
            {
                Trace("CaptureAndRunOcr: result copied to clipboard (EditorOcrShowWindow=false)");
            }
        }
        catch (Exception ex)
        {
            LogError("CaptureAndRunOcr", ex);
            MessageBox.Show($"OCR 失败: {ex.Message}", "ScreenshotTool",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            bitmap.Dispose();
            try { File.Delete(tempPath); } catch { }
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
