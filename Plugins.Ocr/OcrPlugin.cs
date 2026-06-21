using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Plugins.Ocr.Core.Services;
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
    /// Live status read on every menu open. The 插件 submenu paints
    /// the colored dot from this value, so it always reflects the
    /// current state (model files present? service running?).
    /// </summary>
    public PluginStatus Status
    {
        get
        {
            // Unavailable trumps Enabled/Disabled — if the model
            // files are missing the plugin can't run regardless of
            // whether the user has checked the box. The toggle is
            // disabled in the menu when this returns Unavailable.
            if (!ModelsPresent()) return PluginStatus.Unavailable;
            return _service == null ? PluginStatus.Disabled : PluginStatus.Enabled;
        }
    }

    /// <summary>
    /// Check the 3 ONNX model files exist in the plugin's Assets
    /// folder. Cheap (3 file-exists calls), called on every menu
    /// open so the status dot reflects current state — e.g. if the
    /// user copies the models in later, the next menu open shows
    /// the plugin as available.
    /// </summary>
    private static bool ModelsPresent()
    {
        var asmDir = Path.GetDirectoryName(typeof(OcrPlugin).Assembly.Location);
        if (string.IsNullOrEmpty(asmDir)) return false;
        var modelDir = Path.Combine(asmDir, "Assets", "models", "paddleocr");
        return File.Exists(Path.Combine(modelDir, "ch_PP-OCRv4_det_mobile.onnx"))
            && File.Exists(Path.Combine(modelDir, "ch_PP-OCRv4_rec_mobile.onnx"))
            && File.Exists(Path.Combine(modelDir, "ch_ppocr_mobile_v2.0_cls.onnx"));
    }

    /// <summary>
    /// Called when the user enables the plugin via the 插件 submenu
    /// checkbox. Creates the OcrService (ONNX engine is loaded
    /// lazily on first use, but the OcrService itself is cheap),
    /// registers the image-right-click action, and kicks off a
    /// background warmup so the first OCR run is fast.
    ///
    /// Note: the 插件 submenu is toggle-only — the action item
    /// ("提取当前图片文字") lives only in the image viewer's
    /// right-click menu, NOT in the 插件 submenu. The 插件 submenu
    /// only shows the on/off checkbox with a status dot.
    /// </summary>
    public void Activate(IPluginContext context)
    {
        _context = context;
        _service = new OcrService();

        // Only register the image context-menu item. The 插件
        // submenu is reserved for the on/off toggle — the action
        // button doesn't belong there.
        // Tag = this so MainWindow.UnregisterPluginMenuItems can
        // find and remove this item when the user disables.
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
