using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ApertureNeo.Plugins.Ocr.Core.Models;
using ApertureNeo.Plugins.Ocr.Core.Services;
using ApertureNeo.Plugins.Ocr.Ui;
using ApertureNeo.Views;

namespace ApertureNeo.Cli;

/// <summary>
/// `aperture ocr [paths...]` — extract text from one or more images
/// using the PaddleOCR engine (lives in Plugins.Ocr.Core).
///
/// Behavior matrix:
///   ocr a.jpg               headless: OCR -> clipboard -> exit
///   ocr -g a.jpg            single-file GUI: reuse OcrResultWindow
///   ocr a.jpg b.png c.webp  headless: OCR all -> concatenated clipboard -> exit
///   ocr -g a.jpg b.png      multi-file GUI: open HeadlessOcrResultWindow
///
/// The CLI bypasses the OCR plugin's opt-in toggle (in 插件 submenu)
/// by design — `ocr` is its own entry point, not a feature of the
/// viewer. Users who invoke from the right-click shell verb expect
/// it to work regardless of the in-app plugin state.
///
/// OcrService is constructed lazily inside the handler so the model
/// files + ONNX runtime are loaded only when actually needed.
/// </summary>
public sealed class OcrCommand : Command
{
    private readonly Application _app;

    public OcrCommand(Application app)
        : base("ocr", "Extract text from images using PaddleOCR v4 (Chinese-optimized). " +
                      "By default the result is copied to the clipboard; pass -g to open " +
                      "the result window instead.")
    {
        _app = app;

        var guiOption = new Option<bool>(
            aliases: new[] { "-g", "--gui" },
            description: "Open the result window instead of copying to the clipboard.");

        var pathsArgument = new Argument<string[]>(
            name: "paths",
            description: "One or more image file paths to OCR. Supported formats: " +
                         "jpg, jpeg, png, bmp, tif, tiff, webp, heic, heif, jxl, avif, raw, dng.")
        {
            Arity = ArgumentArity.OneOrMore,
        };

        AddOption(guiOption);
        AddArgument(pathsArgument);

        this.SetHandler(async (InvocationContext ctx) =>
        {
            var showGui = ctx.ParseResult.GetValueForOption(guiOption);
            var paths = ctx.ParseResult.GetValueForArgument(pathsArgument);
            int exitCode = await RunAsync(showGui, paths).ConfigureAwait(false);
            ctx.ExitCode = exitCode;
        });
    }

    private async Task<int> RunAsync(bool showGui, string[] paths)
    {
        var service = new OcrService();

        // OCR each file sequentially, capturing both successes and
        // failures. One file's failure does not abort the batch.
        var entries = new List<(string Path, OcrResult Result)>(paths.Length);
        for (int i = 0; i < paths.Length; i++)
        {
            var p = paths[i];
            OcrResult r;
            try
            {
                r = await service.ExtractAsync(p).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                r = new OcrResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
            entries.Add((p, r));
        }

        // -g single-file: reuse the existing OcrResultWindow from
        // Plugins.Ocr.Ui (same UI the plugin's in-app right-click menu
        // shows), so the two entry points share one well-designed
        // editing surface.
        if (showGui && entries.Count == 1)
        {
            return await ShowSingleGuiAsync(entries[0]).ConfigureAwait(false);
        }

        // -g multi-file: open the new HeadlessOcrResultWindow showing
        // every file's result stacked in scrollable sections.
        if (showGui && entries.Count > 1)
        {
            return await ShowMultiGuiAsync(entries).ConfigureAwait(false);
        }

        // Default: concatenate all results and copy to the clipboard.
        // This is the scriptable / shell-verb path — fastest exit.
        return await CopyToClipboardAsync(entries).ConfigureAwait(false);
    }

    private Task<int> ShowSingleGuiAsync((string Path, OcrResult Result) entry)
    {
        var tcs = new TaskCompletionSource<int>();

        _app.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var window = new OcrResultWindow();
                if (entry.Result.IsSuccess)
                    window.SetResult(entry.Result, Path.GetFileName(entry.Path));
                else
                {
                    window.SetError(entry.Result.ErrorMessage ?? "识别失败");
                }
                window.Closed += (_, _) =>
                {
                    _app.Shutdown(0);
                    tcs.TrySetResult(0);
                };
                window.Show();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private Task<int> ShowMultiGuiAsync(IReadOnlyList<(string Path, OcrResult Result)> entries)
    {
        var tcs = new TaskCompletionSource<int>();

        _app.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var window = new HeadlessOcrResultWindow();
                window.SetResults(entries);
                window.Closed += (_, _) =>
                {
                    _app.Shutdown(0);
                    tcs.TrySetResult(0);
                };
                window.Show();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private static Task<int> CopyToClipboardAsync(IReadOnlyList<(string Path, OcrResult Result)> entries)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            var (path, result) = entries[i];
            sb.Append("==== ").Append(Path.GetFileName(path)).AppendLine(" ====");
            if (result.IsSuccess)
                sb.AppendLine(result.FullText);
            else
                sb.Append("[失败] ").AppendLine(result.ErrorMessage ?? "未知错误");
            sb.AppendLine();
        }

        try
        {
            // Clipboard.SetText can throw COMException when another
            // process holds the clipboard lock; swallow + log + exit
            // non-zero so a shell script can detect the failure.
            Clipboard.SetText(sb.ToString());
            Application.Current?.Shutdown(0);
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            try { System.Console.Error.WriteLine("clipboard failed: " + ex.Message); } catch { }
            Application.Current?.Shutdown(1);
            return Task.FromResult(1);
        }
    }
}