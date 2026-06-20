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
    private OcrResultWindow? _window;

    public string Name => "PaddleOCR 文字提取";

    public string Description => "使用 PaddleOCR v4 提取图片中的中英文";

    public void Initialize(IPluginContext context)
    {
        _service = new OcrService();

        var item = new System.Windows.Controls.MenuItem { Header = "提取当前图片文字 (OCR)" };
        item.Click += async (_, _) => await OnExtractAsync(context).ConfigureAwait(false);
        context.RegisterMenuItem(item);

        var ctxItem = new System.Windows.Controls.MenuItem { Header = "提取当前图片文字" };
        ctxItem.Click += async (_, _) => await OnExtractAsync(context).ConfigureAwait(false);
        context.RegisterContextMenuItem(ctxItem);

        _ = Task.Run(() => _service.WarmupAsync());
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

        _window ??= new OcrResultWindow();
        _window.SetStatus("识别中…");
        _window.Show();
        _window.Activate();

        var result = await _service.ExtractAsync(path).ConfigureAwait(false);
        if (_window == null) return;

        if (!result.IsSuccess)
        {
            _window.SetError(result.ErrorMessage ?? "识别失败");
            return;
        }

        _window.SetResult(result, Path.GetFileName(path));
    }
}
