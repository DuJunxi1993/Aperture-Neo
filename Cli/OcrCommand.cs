using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        int successCount = 0;
        int failureCount = 0;
        string firstError = "";
        OcrResult? firstResult = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var (path, result) = entries[i];
            // The `==== filename ====` header is only useful when
            // multiple files are concatenated into one clipboard
            // payload (otherwise the user knows which file they
            // just OCR'd). Single-file output omits it to keep the
            // clipboard contents clean.
            if (entries.Count > 1)
            {
                sb.Append("==== ").Append(Path.GetFileName(path)).AppendLine(" ====");
            }
            if (result.IsSuccess)
            {
                successCount++;
                if (firstResult == null) firstResult = result;
                sb.AppendLine(result.FullText);
            }
            else
            {
                failureCount++;
                firstError = result.ErrorMessage ?? "未知错误";
                sb.Append("[失败] ").AppendLine(firstError);
            }
            sb.AppendLine();
        }

        bool clipboardOk = false;
        try
        {
            // Clipboard.SetText can throw COMException when another
            // process holds the clipboard lock. We log + exit 0
            // anyway because the OCR itself succeeded — the
            // 448-char result is in memory; the user can rerun
            // and try the clipboard write again. Returning exit 1
            // here made shell scripts think the OCR failed.
            Clipboard.SetText(sb.ToString());
            clipboardOk = true;
        }
        catch (Exception ex)
        {
            try { System.Console.Error.WriteLine("clipboard failed: " + ex.Message); } catch { }
            // Fall through: the OCR succeeded, the clipboard write
            // didn't. The user can pipe the OCR result to a file
            // (`aperture ocr ... > out.txt`) to bypass the clipboard.
        }

        // Fire Windows Toast notification so the user gets feedback.
        // The clipboard path is silent otherwise — without this,
        // a right-click → "快速 OCR 到剪贴板" leaves the user
        // staring at nothing for 3-10s wondering if it's working.
        // The toast appears via a separate powershell.exe process
        // (notify.ps1) so we can Shutdown immediately without
        // racing the toast's lifetime.
        if (clipboardOk)
        {
            if (successCount > 0)
            {
                var title = "OCR 完成";
                string msg;
                if (entries.Count == 1 && firstResult != null)
                {
                    // Single-file: include the character count so the
                    // user can gauge how much was extracted at a glance.
                    var len = firstResult.FullText?.Length ?? 0;
                    msg = $"已复制 {len} 个字符到剪贴板";
                }
                else
                {
                    // Multi-file: summarize counts. Failure count
                    // appended only when non-zero so the happy path
                    // stays short.
                    msg = failureCount > 0
                        ? $"已复制 {successCount} 个文件的结果,{failureCount} 个失败"
                        : $"已复制 {successCount} 个文件的结果";
                }
                ShowToast(title, msg);
            }
            else
            {
                ShowToast("OCR 失败", firstError);
            }
        }
        else
        {
            ShowToast("OCR 失败", "剪贴板写入失败");
        }

        Application.Current?.Shutdown(0);
        return Task.FromResult(0);
    }

    /// <summary>
    /// Show a Windows 10/11 toast notification by launching the
    /// bundled notify.ps1 in a separate PowerShell process.
    /// Fire-and-forget — if the toast fails (WinRT missing,
    /// PowerShell unavailable, no toast runtime), the clipboard
    /// result is still valid; the user just won't see a
    /// confirmation.
    /// </summary>
    private static void ShowToast(string title, string message)
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (exeDir == null) return;
            var scriptPath = Path.Combine(exeDir, "notify.ps1");
            if (!File.Exists(scriptPath))
            {
                DebugLog.Write("OcrCommand", "notify.ps1 not found at " + scriptPath);
                return;
            }

            // Quote-escape the title and message for the powershell
            // command line. The script's [Parameter(Mandatory)] will
            // bind them positionally. Embedded `"` characters are
            // escaped as `\"` to keep the powershell tokenizer happy.
            var escTitle = title.Replace("\"", "\\\"");
            var escMessage = message.Replace("\"", "\\\"");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" \"{escTitle}\" \"{escMessage}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            DebugLog.Write("OcrCommand", "ShowToast failed", ex);
        }
    }
}