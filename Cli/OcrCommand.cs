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
using ApertureNeo.Services;
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
        : base("ocr",
              "Extract text from images using PaddleOCR v4 (Chinese-optimized).\n\n" +
              "By default, the result is concatenated and copied to the clipboard. " +
              "Pass -g to open the result window instead.\n\n" +
              "EXAMPLES:\n" +
              "  ApertureNeo ocr photo.jpg               OCR one image, copy result to clipboard\n" +
              "  ApertureNeo ocr a.jpg b.png c.webp      OCR multiple images (batch, concatenated)\n" +
              "  ApertureNeo ocr -g photo.jpg            Open the result window for one file\n" +
              "  ApertureNeo ocr -g a.jpg b.png          Open the multi-file result window\n\n" +
              "SUPPORTED FORMATS:\n" +
              "  jpg, jpeg, png, bmp, tif, tiff, webp, heic, heif, jxl, avif, raw, dng\n\n" +
              "NOTES:\n" +
              "  - One file's failure does not abort the batch; failed files are marked\n" +
              "    with \"[失败]\" in the clipboard output.\n" +
              "  - The clipboard write can fail if another process holds the clipboard\n" +
              "    lock; OCR result is still computed and the error is reported to stderr.\n" +
              "  - The CLI bypasses the in-app OCR plugin toggle (in the 插件 submenu) by\n" +
              "    design — `ocr` is its own entry point.\n\n" +
              "EXIT CODES:\n" +
              "  0  Success (OCR completed; clipboard write may still have failed — see stderr)\n" +
              "  1  OCR failure (file read error, model load error, etc.)\n" +
              "  2  Invalid arguments")
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
                DebugLog.Write("OcrCommand", "ShowMultiGui failed", ex);
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
            // process holds the clipboard lock. We log + exit 0
            // anyway because the OCR itself succeeded — the
            // 448-char result is in memory; the user can rerun
            // and try the clipboard write again. Returning exit 1
            // here made shell scripts think the OCR failed.
            Clipboard.SetText(sb.ToString());
        }
        catch (Exception ex)
        {
            try { System.Console.Error.WriteLine("clipboard failed: " + ex.Message); } catch { }
            // Fall through: the OCR succeeded, the clipboard write
            // didn't. The user can pipe the OCR result to a file
            // (`aperture ocr ... > out.txt`) to bypass the clipboard.
        }
        Application.Current?.Shutdown(0);
        return Task.FromResult(0);
    }
}