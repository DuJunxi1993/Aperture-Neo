using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace ApertureNeo.Services;

/// <summary>
/// System-tray icon and menu. Uses <see cref="WinForms.NotifyIcon"/>
/// under the hood — the same API WPF-UI 3.x's INotifyIcon wraps.
/// Implemented directly here (rather than via WPF-UI's interface)
/// so the dependency surface stays small and the icon-handling
/// code is easy to audit.
///
/// Lifecycle:
/// <list type="number">
///   <item><see cref="Show"/> — call once at app startup. Creates
///         the NotifyIcon, hooks the context menu, and adds it
///         to the system tray.</item>
///   <item><see cref="Hide"/> — call when the user wants to
///         temporarily remove the icon (rare; we keep it
///         persistent while the process is alive).</item>
///   <item><see cref="Dispose"/> — call at process shutdown.
///         Removes the icon and releases resources.</item>
/// </list>
///
/// Events:
/// <list type="bullet">
///   <item><see cref="ShowMainRequested"/> — user double-clicked
///         the icon or picked "显示主窗口".</item>
///   <item><see cref="ExitRequested"/> — user picked "退出".</item>
///   <item><see cref="CaptureAreaRequested"/> — user picked
///         "截图".</item>
///   <item><see cref="CaptureOcrRequested"/> — user picked
///         "OCR".</item>
///   <item><see cref="OpenSettingsRequested"/> — user picked
///         "设置".</item>
/// </list>
/// </summary>
public sealed class TrayService : IDisposable
{
    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ContextMenuStrip? _menu;

    public event EventHandler? ShowMainRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? CaptureAreaRequested;
    public event EventHandler? CaptureOcrRequested;
    public event EventHandler? OpenSettingsRequested;

    /// <summary>True once <see cref="Show"/> has succeeded.
    /// Useful for guarded early exits in tests.</summary>
    public bool IsVisible => _notifyIcon?.Visible == true;

    /// <summary>Build and display the tray icon + menu.
    /// Idempotent — calling twice is a no-op.</summary>
    public void Show()
    {
        if (_notifyIcon != null) return;

        _menu = BuildMenu();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Aperture Neo",
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Hide the tray icon without disposing. Used in
    /// rare cases (e.g. a maintenance mode) where we want the
    /// service alive but the icon temporarily gone.</summary>
    public void Hide()
    {
        if (_notifyIcon != null) _notifyIcon.Visible = false;
    }

    /// <summary>Pop a balloon notification (Win10/11 toast
    /// fallback). No-op on systems that suppress balloons.</summary>
    public void ShowBalloon(string title, string text, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, text, icon);
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.DoubleClick -= (_, _) => ShowMainRequested?.Invoke(this, EventArgs.Empty);
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        if (_menu != null)
        {
            _menu.Dispose();
            _menu = null;
        }
    }

    // ---- internals ----

    private WinForms.ContextMenuStrip BuildMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        var capture = new WinForms.ToolStripMenuItem("截图");
        capture.Click += (_, _) => CaptureAreaRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(capture);

        var ocr = new WinForms.ToolStripMenuItem("OCR");
        ocr.Click += (_, _) => CaptureOcrRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(ocr);

        var settings = new WinForms.ToolStripMenuItem("设置...");
        settings.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(settings);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var show = new WinForms.ToolStripMenuItem("显示主窗口");
        show.Click += (_, _) => ShowMainRequested?.Invoke(this, EventArgs.Empty);
        // ToolStripMenuItem doesn't expose DefaultItem directly;
        // the "default" item (Enter-key activated when the menu is
        // open) is the one whose font is bold. Setting it visually
        // is enough for the affordance — users learn the convention.
        show.Font = new System.Drawing.Font(show.Font ?? System.Drawing.SystemFonts.MenuFont, System.Drawing.FontStyle.Bold);
        menu.Items.Add(show);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exit = new WinForms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exit);

        return menu;
    }

    /// <summary>Load the tray icon. Falls back to the system
    /// application icon if the embedded one isn't available.</summary>
    private static Icon LoadTrayIcon()
    {
        // The main exe ships with <ApplicationIcon>Assets\apertureneo.ico.
        // Extract from the running module so the tray matches the
        // taskbar/title-bar icon. If extraction fails, fall back to
        // the stock application icon (still visible, just generic).
        try
        {
            var exePath = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
        }
        catch { /* fall through */ }
        return SystemIcons.Application;
    }
}