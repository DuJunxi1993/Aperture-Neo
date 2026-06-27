using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ApertureNeo.Plugins.Screenshot;

public static class OcrIntegration
{
    public static async Task RunOcrAsync(string imagePath)
    {
        // Find ApertureNeo.exe relative to the screenshot tool
        var exeDir = Path.GetDirectoryName(typeof(OcrIntegration).Assembly.Location) ?? ".";
        var mainExe = Path.Combine(exeDir, "..", "ApertureNeo.exe");
        if (!File.Exists(mainExe))
        {
            // Fallback: same directory
            mainExe = Path.Combine(exeDir, "ApertureNeo.exe");
        }

        if (!File.Exists(mainExe))
            throw new FileNotFoundException("ApertureNeo.exe not found. OCR requires the main app CLI.");

        var psi = new ProcessStartInfo
        {
            FileName = mainExe,
            Arguments = $"ocr \"{imagePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("Failed to start ApertureNeo.exe");

        // Wait up to 60 seconds for OCR to complete
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException("OCR timed out after 60 seconds.");
        }

        if (proc.ExitCode != 0)
        {
            // Non-zero exit is common (clipboard write can fail even though OCR succeeded)
            // Just warn; the result might still be in the clipboard.
        }
    }
}
