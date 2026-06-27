using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using ApertureNeo.Services;

namespace ApertureNeo.Plugins.Screenshot;

public sealed class ScreenshotPlugin : IPluginModule
{
    private IPluginContext? _context;

    public string Name => "屏幕截图";
    public string Description => "区域/窗口/全屏截图,编辑并调用 OCR";

    public PluginStatus Status =>
        GetHostExePath() != null
            ? PluginStatus.Disabled
            : PluginStatus.Unavailable;

    public void Activate(IPluginContext context)
    {
        _context = context;

        var menuItem = new MenuItem
        {
            Header = "截图 (区域)",
            Tag = this,
        };
        menuItem.Click += (_, _) => LaunchStandalone("--area");
        context.ViewSlot(ShellRegions.ViewerContextMenu, menuItem);

        var fullItem = new MenuItem
        {
            Header = "截图 (全屏)",
            Tag = this,
        };
        fullItem.Click += (_, _) => LaunchStandalone("--fullscreen");
        context.ViewSlot(ShellRegions.ViewerContextMenu, fullItem);
    }

    public void Deactivate()
    {
        _context?.ClearSlots(this);
        _context = null;
    }

    private static void LaunchStandalone(string mode)
    {
        var hostPath = GetHostExePath();
        if (hostPath == null) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = mode,
                UseShellExecute = false,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            DebugLog.Write("ScreenshotPlugin", $"launch failed: {ex.Message}");
        }
    }

    private static string? GetHostExePath()
    {
        var asmDir = Path.GetDirectoryName(typeof(ScreenshotPlugin).Assembly.Location);
        if (string.IsNullOrEmpty(asmDir)) return null;

        // The standalone EXE sits next to the plugin DLL in Plugins\
        var exePath = Path.Combine(asmDir, "ScreenshotTool.exe");
        if (File.Exists(exePath)) return exePath;

        return exePath;
    }
}
