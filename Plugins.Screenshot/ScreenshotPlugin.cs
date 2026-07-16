using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureNeo.Helpers;
using ApertureNeo.Services;

namespace ApertureNeo.Plugins.Screenshot;

/// <summary>
/// Tray-resident screenshot plugin. Stage 5 rewrite:
/// <list type="bullet">
///   <item>Registers three global hotkeys (<c>ScreenshotPlugin
///       .CaptureArea</c> / <c>ScreenshotPlugin.CaptureOcr</c> /
///       <c>ScreenshotPlugin.CaptureFullscreen</c>) via
///       <see cref="IShortcutService"/> during Activate.</item>
///   <item>Routes the trigger to <see cref="CaptureService"/>
///       in-process — no more <c>Process.Start ScreenshotTool.exe</c>.</item>
///   <item>Right-click menu offers "截图 (区域)" — calls the
///       in-process service. The "截图 (全屏)" right-click entry
///       was removed (the PrintScreen hotkey covers the same
///       need; the tray menu still has the entry).</item>
///   <item>The "OCR 显示结果窗口" CheckBox moves to the
///       settings panel (decision 4) — this plugin no longer
///       contributes that menu item.</item>
/// </list>
///
/// The legacy <c>LaunchStandalone</c> path is removed: the
/// standalone <c>ScreenshotTool.exe</c> still exists for external
/// CLI callers (decision 3 — preserve CLI friendliness), but the
/// main app no longer spawns it.
/// </summary>
public sealed class ScreenshotPlugin : IPluginModule
{
    private IPluginContext? _context;

    public string Name => "屏幕截图";
    public string Description => "区域/窗口/全屏截图,编辑并调用 OCR";

    /// <summary>
    /// Status is read on every menu open by PluginShellViewModel.
    /// <list type="bullet">
    ///   <item>Before Activate(): Disabled (no context yet).</item>
    ///   <item>After Activate() with a working CaptureService:
    ///         Enabled.</item>
    ///   <item>After Activate() but the capture service is
    ///         missing (DI graph broken): Unavailable, so the
    ///         UI greys out the toggle.</item>
    /// </list>
    /// </summary>
    public PluginStatus Status
    {
        get
        {
            if (_context == null) return PluginStatus.Disabled;
            return _context.GetService<CaptureService>() != null
                ? PluginStatus.Enabled
                : PluginStatus.Unavailable;
        }
    }

    public void Activate(IPluginContext context)
    {
        _context = context;

        // Right-click viewer menu: two items, both routed
        // through the in-process CaptureService. Status is
        // already validated as non-Unavailable by PluginShellViewModel
        // (which calls Status before showing the toggle), so
        // the menu items can assume the service is present.
        var capture = context.GetService<CaptureService>();
        var settings = context.GetService<ISettingsStore>();
        var shortcutService = context.GetService<IShortcutService>();

        if (capture != null)
        {
            var areaMenuItem = new MenuItem
            {
                Header = "截图 (区域)",
                Tag = this,
            };
            areaMenuItem.Click += (_, _) => _ = capture.RunCaptureAreaAsync();
            context.ViewSlot(ShellRegions.ViewerContextMenu, areaMenuItem);

            // Right-click "Screenshot (Fullscreen)" menu item was
            // removed per user request — users wanting fullscreen
            // capture use the PrintScreen hotkey or the tray menu.
            // The tray-captured fullscreen flow also depends on
            // the in-process CaptureService.RunCaptureFullscreenAsync
            // (still wired for the hotkey below) so we just skip
            // exposing it in the viewer's context menu.
        }

        // Global hotkey registration. Skipped if the shortcut
        // service or settings aren't present (the plugin
        // gracefully degrades to "menu-only" rather than
        // refusing to activate).
        if (shortcutService != null && settings != null)
        {
            TryRegisterShortcut(shortcutService, settings, capture,
                "ScreenshotPlugin.CaptureArea", "Ctrl+Alt+A",
                c => c?.RunCaptureAreaAsync());
            TryRegisterShortcut(shortcutService, settings, capture,
                "ScreenshotPlugin.CaptureOcr", "Ctrl+Alt+T",
                c => c?.RunCaptureAreaOcrAsync());
            TryRegisterShortcut(shortcutService, settings, capture,
                "ScreenshotPlugin.CaptureFullscreen", "PrintScreen",
                c => c?.RunCaptureFullscreenAsync());
        }
    }

    public void Deactivate()
    {
        // Unregister our shortcuts. Tolerate failures — the
        // service might already be disposed during shutdown.
        try
        {
            var shortcutService = _context?.GetService<IShortcutService>();
            shortcutService?.Unregister("ScreenshotPlugin.CaptureArea");
            shortcutService?.Unregister("ScreenshotPlugin.CaptureOcr");
        }
        catch (Exception ex)
        {
            DebugLog.Write("ScreenshotPlugin",
                $"unregister failed: {ex.Message}");
        }

        _context?.ClearSlots(this);
        _context = null;
    }

    /// <summary>
    /// Resolve a KeyGesture from the user's settings (or fall
    /// back to the supplied default), then register it. Failure
    /// to parse the user's stored string falls back silently —
    /// bad data shouldn't break the plugin. Registration failure
    /// (Win32 says the combo is already taken) is logged AND
    /// surfaced to the user as a MessageBox so they know to
    /// change the binding in Settings → 快捷键. Previously the
    /// failure was only logged, which meant silent-fail
    /// behaviour with no user-visible feedback.
    /// </summary>
    private static void TryRegisterShortcut(
        IShortcutService shortcutService,
        ISettingsStore settings,
        CaptureService? capture,
        string id, string defaultGesture,
        Action<CaptureService?> onFire)
    {
        var stored = settings.GetShortcuts();
        var raw = stored.TryGetValue(id, out var v) ? v : defaultGesture;
        try
        {
            var converter = new KeyGestureConverter();
            var gesture = (KeyGesture?)converter.ConvertFromString(raw);
            if (gesture == null)
            {
                DebugLog.Write("ScreenshotPlugin",
                    $"could not parse shortcut '{raw}' for {id}; skipping");
                return;
            }
            var ok = shortcutService.Register(id, gesture, () => onFire(capture));
            if (!ok)
            {
                DebugLog.Write("ScreenshotPlugin",
                    $"RegisterHotKey failed for {id} = {raw} (combo taken?)");
                // Show a one-time message per failed binding so
                // the user understands the hotkey doesn't work
                // and how to fix it. Wrapped in try/catch
                // because MessageBox itself can fail in
                // headless / CI scenarios.
                try
                {
                    var display = LabelFor(id);
                    System.Windows.MessageBox.Show(
                        $"快捷键 {raw} 注册失败,可能已被其他程序占用。\n\n请到\"设置 → 快捷键\"中更换 {display} 的绑定。",
                        "Aperture Neo — 快捷键冲突",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
                catch { /* headless — diagnostic log already written */ }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write("ScreenshotPlugin",
                $"TryRegisterShortcut({id}) threw: {ex.Message}");
        }
    }

    /// <summary>User-friendly label for the shortcut id used
    /// in the conflict message.</summary>
    private static string LabelFor(string id) => id switch
    {
        "ScreenshotPlugin.CaptureArea" => "截图选区",
        "ScreenshotPlugin.CaptureOcr" => "OCR 选区",
        "ScreenshotPlugin.CaptureFullscreen" => "全屏截图",
        _ => id,
    };
}