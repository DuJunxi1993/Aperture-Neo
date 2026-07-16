using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApertureNeo.Plugins.Ocr.Core.Models;
using RapidOCRSharpOnnx.Configurations;
using RapidOCRSharpOnnx.Providers;
using RapidOCRSharpOnnx.Utils;
using OcrResult = ApertureNeo.Plugins.Ocr.Core.Models.OcrResult;

namespace ApertureNeo.Plugins.Ocr.Core.Services;

public sealed class OcrService : IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _inferLock = new(1, 1);
    private global::RapidOCRSharpOnnx.RapidOCRSharp? _engine;
    private bool _disposed;

    public bool IsLoaded => _engine != null;

    public string? ModelDirectory { get; }

    public OcrService()
    {
        // Models live in Assets/models/paddleocr next to the DLL
        // containing this type. Two copy paths land them there:
        //   1. Plugin path: the plugin's DeployToMain target copies
        //      Core's $(OutDir) (which includes Assets/models/* via
        //      CopyToOutputDirectory) into bin/Plugins/. Core.dll +
        //      models end up side-by-side.
        //   2. Main-exe CLI path: ApertureNeo.csproj has a
        //      CopyOcrModelsForCli target that copies the same
        //      models into the main exe's bin folder so `aperture
        //      ocr ...` can find them when Ocr.Core is referenced
        //      directly (not via PluginLoadContext).
        var asmDir = Path.GetDirectoryName(typeof(OcrService).Assembly.Location);
        ModelDirectory = !string.IsNullOrEmpty(asmDir)
            ? Path.Combine(asmDir, "Assets", "models", "paddleocr")
            : Path.Combine(AppContext.BaseDirectory, "Assets", "models", "paddleocr");
    }

    public async Task WarmupAsync(CancellationToken ct = default)
    {
        await EnsureEngineAsync(ct).ConfigureAwait(false);
    }

    public async Task<OcrResult> ExtractAsync(string imagePath, CancellationToken ct = default)
    {
        if (!File.Exists(imagePath))
            return new OcrResult { IsSuccess = false, ErrorMessage = "文件不存在" };

        try
        {
            var engine = await EnsureEngineAsync(ct).ConfigureAwait(false);

            await _inferLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var raw = engine.RecognizeText(imagePath);
                sw.Stop();

                var lines = new List<OcrLine>();
                if (raw.RecResult?.Data is { } recs)
                {
                    foreach (var r in recs)
                    {
                        if (!string.IsNullOrWhiteSpace(r.Label))
                            lines.Add(new OcrLine { Text = r.Label, Confidence = r.Score });
                    }
                }

                return new OcrResult
                {
                    IsSuccess = true,
                    // RapidOCRSharpOnnx's TextBlocks is space-joined, not
                    // newline-joined, so 换行 / 不换行 toggling has nothing
                    // to do. Build FullText from the per-line recognition
                    // results instead — one \n per detected text row.
                    FullText = lines.Count > 0
                        ? string.Join("\n", lines.Select(l => l.Text))
                        : (raw.TextBlocks?.Trim() ?? string.Empty),
                    Lines = lines,
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }
            finally
            {
                _inferLock.Release();
            }
        }
        catch (Exception ex)
        {
            return new OcrResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<global::RapidOCRSharpOnnx.RapidOCRSharp> EnsureEngineAsync(CancellationToken ct)
    {
        if (_engine != null) return _engine;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_engine != null) return _engine;

            var detPath = Path.Combine(ModelDirectory!, "ch_PP-OCRv4_det_mobile.onnx");
            var recPath = Path.Combine(ModelDirectory!, "ch_PP-OCRv4_rec_mobile.onnx");
            var clsPath = Path.Combine(ModelDirectory!, "ch_ppocr_mobile_v2.0_cls.onnx");

            if (!File.Exists(detPath) || !File.Exists(recPath) || !File.Exists(clsPath))
                throw new FileNotFoundException($"OCR 模型文件缺失,请检查 {ModelDirectory}");

            var config = new OcrConfig(detPath, recPath, LangRec.CH, OCRVersion.PPOCRV4, clsPath);
            _engine = new global::RapidOCRSharpOnnx.RapidOCRSharp(new ExecutionProviderCPU(config));
            return _engine;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Dispose();
        _engine = null;
        _initLock.Dispose();
        _inferLock.Dispose();
    }
}
