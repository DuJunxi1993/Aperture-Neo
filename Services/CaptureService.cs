using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ApertureNeo.Plugins.Ocr.Core.Models;
using ApertureNeo.Plugins.Ocr.Core.Services;
using ApertureNeo.Views;
using ApertureNeo.Helpers;

namespace ApertureNeo.Services;

public sealed class CaptureService
{
    private readonly ISettingsStore _settings;
    private readonly Func<Window?> _mainWindowAccessor;

    public CaptureService(ISettingsStore settings, Func<Window?> mainWindowAccessor)
    {
        _settings = settings;
        _mainWindowAccessor = mainWindowAccessor;
    }

    public Task<Bitmap?> RunCaptureAreaAsync() =>
        RunCaptureAreaAsync(autoOcr: false);

    public Task<Bitmap?> RunCaptureAreaOcrAsync() =>
        RunCaptureAreaAsync(autoOcr: true);

    public Task<Bitmap?> RunCaptureFullscreenAsync() =>
        RunCaptureFullscreenAsyncImpl();

    private Task<Bitmap?> RunCaptureAreaAsync(bool autoOcr)
    {
        var tcs = new TaskCompletionSource<Bitmap?>();
        _ = RunOnWpfUiAsync(() =>
        {
            DebugLog.Write("CaptureService", "RunCaptureAreaAsync: action entered");
            try
            {
                var overlay = new RegionOverlay();
                var mainWindow = _mainWindowAccessor();
                if (mainWindow is { IsVisible: true })
                    overlay.Owner = mainWindow;

                DebugLog.Write("CaptureService", "RunCaptureAreaAsync: calling overlay.ShowDialog()");
                overlay.ShowDialog();
                DebugLog.Write("CaptureService", $"RunCaptureAreaAsync: overlay.ShowDialog() returned, Confirmed={overlay.Confirmed}");

                if (!overlay.Confirmed)
                {
                    DebugLog.Write("CaptureService", "RunCaptureAreaAsync: not confirmed, exiting");
                    tcs.TrySetResult(null);
                    return;
                }

                DebugLog.Write("CaptureService", $"RunCaptureAreaAsync: capturing, IsFullscreen={overlay.IsFullscreen}, Region={overlay.SelectedRegion}");
                Bitmap? bitmap = overlay.IsFullscreen
                    ? CaptureOnContextIdle(CaptureEngine.CaptureFullscreen)
                    : CaptureOnContextIdle(() => CaptureEngine.CaptureRegion(overlay.SelectedRegion!.Value));

                if (bitmap == null)
                {
                    DebugLog.Write("CaptureService", "RunCaptureAreaAsync: bitmap is null after capture");
                    tcs.TrySetResult(null);
                    return;
                }

                DebugLog.Write("CaptureService", $"RunCaptureAreaAsync: captured bitmap {bitmap.Width}x{bitmap.Height}, OcrRequested={overlay.OcrRequested}");

                if (autoOcr || overlay.OcrRequested)
                {
                    DebugLog.Write("CaptureService", "RunCaptureAreaAsync: OcrRequested=true, running OCR directly");
                    _ = RunOcrOnBitmapAsync(bitmap);
                    tcs.TrySetResult(null);
                    return;
                }

                ShowEditorDialog(bitmap, autoOcr, tcs);
            }
            catch (Exception ex)
            {
                DebugLog.Write("CaptureService",
                    $"RunCaptureAreaAsync failed: {ex.GetType().Name}: {ex.Message}");
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
    }

    private Task<Bitmap?> RunCaptureFullscreenAsyncImpl()
    {
        var tcs = new TaskCompletionSource<Bitmap?>();
        _ = RunOnWpfUiAsync(() =>
        {
            DebugLog.Write("CaptureService", "RunCaptureFullscreenAsyncImpl: action entered");
            try
            {
                var mainWindow = _mainWindowAccessor();
                if (mainWindow == null)
                {
                    DebugLog.Write("CaptureService", "RunCaptureFullscreenAsyncImpl: mainWindow is null");
                    tcs.TrySetResult(null);
                    return;
                }

                DebugLog.Write("CaptureService", "RunCaptureFullscreenAsyncImpl: capturing fullscreen");
                Bitmap? bitmap;
                try
                {
                    bitmap = CaptureOnContextIdle(CaptureEngine.CaptureFullscreen);
                }
                catch (Exception ex)
                {
                    DebugLog.Write("CaptureService",
                        $"fullscreen capture failed: {ex.GetType().Name}: {ex.Message}");
                    tcs.TrySetResult(null);
                    return;
                }

                if (bitmap == null)
                {
                    DebugLog.Write("CaptureService", "RunCaptureFullscreenAsyncImpl: bitmap is null");
                    tcs.TrySetResult(null);
                    return;
                }

                DebugLog.Write("CaptureService", $"RunCaptureFullscreenAsyncImpl: captured {bitmap.Width}x{bitmap.Height}");
                ShowEditorDialog(bitmap, autoOcr: false, tcs);
            }
            catch (Exception ex)
            {
                DebugLog.Write("CaptureService",
                    $"RunCaptureFullscreenAsync failed: {ex.GetType().Name}: {ex.Message}");
                tcs.TrySetResult(null);
            }
        });
        return tcs.Task;
    }

    private void ShowEditorDialog(Bitmap bitmap, bool autoOcr,
        TaskCompletionSource<Bitmap?> tcs)
    {
        EditorWindow? editor = null;
        try
        {
            editor = new EditorWindow(bitmap, _settings);
            if (autoOcr) editor.AutoOcrOnShow = true;

            var mainWindow = _mainWindowAccessor();
            if (mainWindow is { IsVisible: true })
                editor.Owner = mainWindow;

            DebugLog.Write("CaptureService", "ShowEditorDialog: showing editor");
            editor.Topmost = true;
            editor.ShowDialog();
            DebugLog.Write("CaptureService", "ShowEditorDialog: editor closed");

            tcs.TrySetResult(bitmap);
        }
        catch (Exception ex)
        {
            DebugLog.Write("CaptureService",
                $"ShowEditorDialog failed: {ex.GetType().Name}: {ex.Message}");
            try { editor?.Close(); } catch { }
            try { bitmap.Dispose(); } catch { }
            tcs.TrySetResult(null);
        }
    }

    private static Task RunOnWpfUiAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            DebugLog.Write("CaptureService", "RunOnWpfUiAsync: dispatcher is null, running sync");
            action();
            return Task.CompletedTask;
        }
        DebugLog.Write("CaptureService", "RunOnWpfUiAsync: posting to dispatcher");
        return dispatcher.BeginInvoke(action).Task;
    }

    private static Bitmap? CaptureOnContextIdle(Func<Bitmap> capture)
    {
        Bitmap? bitmap = null;
        Exception? error = null;
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            DebugLog.Write("CaptureService", "CaptureOnContextIdle: capture action running");
            try { bitmap = capture(); }
            catch (Exception ex) { error = ex; }
            DebugLog.Write("CaptureService", $"CaptureOnContextIdle: done, bitmap={(bitmap != null ? $"{bitmap.Width}x{bitmap.Height}" : "null")}, error={error?.GetType().Name ?? "none"}");
            frame.Continue = false;
        }), DispatcherPriority.Normal);
        DebugLog.Write("CaptureService", "CaptureOnContextIdle: pushing frame");
        Dispatcher.PushFrame(frame);
        DebugLog.Write("CaptureService", "CaptureOnContextIdle: frame returned");
        if (error != null)
        {
            DebugLog.Write("CaptureService", $"capture threw: {error.Message}");
            System.Windows.MessageBox.Show(
                $"截图失败: {error.Message}",
                "Aperture Neo", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        return bitmap;
    }

    public async Task RunOcrOnBitmapAsync(Bitmap bitmap)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(), "ApertureNeo", "screenshots",
            $"screenshot-ocr-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        try
        {
            bitmap.Save(tempPath, ImageFormat.Png);

            var ocr = new OcrService();
            OcrResult result = await ocr.ExtractAsync(tempPath).ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                System.Windows.MessageBox.Show(
                    $"OCR 失败: {result.ErrorMessage}",
                    "Aperture Neo", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try { Clipboard.SetText(result.FullText); } catch { }

            if (_settings.EditorOcrShowWindow)
            {
                var win = new ApertureNeo.Plugins.Ocr.Ui.OcrResultWindow();
                win.SetResult(result, Path.GetFileName(tempPath));
                win.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("CaptureService", $"RunOcrOnBitmapAsync threw: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"OCR 失败: {ex.Message}",
                "Aperture Neo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { bitmap.Dispose(); } catch { }
            try { File.Delete(tempPath); } catch { }
        }
    }
}
