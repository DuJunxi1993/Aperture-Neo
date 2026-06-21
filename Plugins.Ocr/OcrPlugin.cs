using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Plugins.Ocr.Services;
using ApertureNeo.Plugins.Ocr.Ui;
using ApertureNeo.Services;

namespace ApertureNeo.Plugins.Ocr;

public sealed class OcrPlugin : IPlugin
{
    private OcrService? _service;
    private IPluginContext? _context;

    public string Name => "PaddleOCR 文字提取";

    public string Description => "使用 PaddleOCR v4 提取图片中的中英文";

    /// <summary>
    /// Called when the user enables the plugin via the 插件 submenu
    /// checkbox. Creates the OcrService (ONNX engine is loaded
    /// lazily on first use, but the OcrService itself is cheap),
    /// registers menu items, and kicks off a background warmup so
    /// the first OCR run is fast.
    /// </summary>
    public void Activate(IPluginContext context)
    {
        _context = context;
        _service = new OcrService();

        // Tag = this so MainWindow.UnregisterPluginMenuItems can find
        // and remove these items when the user disables the plugin.
        var item = new System.Windows.Controls.MenuItem
        {
            Header = "提取当前图片文字 (OCR)",
            Tag = this,
        };
        item.Click += async (_, _) => await OnExtractAsync(context);
        context.RegisterMenuItem(item);

        var ctxItem = new System.Windows.Controls.MenuItem
        {
            Header = "提取当前图片文字",
            Tag = this,
        };
        ctxItem.Click += async (_, _) => await OnExtractAsync(context);
        context.RegisterContextMenuItem(ctxItem);

        // Background warmup: load the 3 ONNX models (~16MB) into
        // memory now so the first OCR run is fast. Fire-and-forget;
        // the user can still use the rest of the app while this
        // runs.
        _ = Task.Run(() => _service.WarmupAsync());
    }

    /// <summary>
    /// Called when the user disables the plugin. Disposes the
    /// OcrService (releases the ONNX engine + model memory) and
    /// asks the context to remove our menu items.
    /// </summary>
    public void Deactivate()
    {
        _service?.Dispose();
        _service = null;
        _context?.UnregisterPluginMenuItems(this);
        _context = null;
    }

    private async Task OnExtractAsync(IPluginContext context)
    {
        var path = context.CurrentImagePath;
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("当前没有打开图片。", "PaddleOCR", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_service == null) return;

        var window = new OcrResultWindow();
        try
        {
            window.SetStatus("识别中…");
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            DebugLog.Write("OcrPlugin", $"show failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        try
        {
            var result = await _service.ExtractAsync(path);

            if (!result.IsSuccess)
                window.SetError(result.ErrorMessage ?? "识别失败");
            else
                window.SetResult(result, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            DebugLog.Write("OcrPlugin", $"extract failed: {ex.GetType().Name}: {ex.Message}");
            try { window.SetError(ex.Message); } catch { }
        }
    }
}
