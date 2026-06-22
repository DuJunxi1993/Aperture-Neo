using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ApertureNeo.Views;

/// <summary>
/// 44px title bar extracted from MainWindow.xaml. Owns its own
/// drag-to-move, double-click-to-maximize, and window-control
/// buttons (min/max/close) — all of which are self-contained
/// state changes that don't need to leave the title bar.
///
/// Cross-component actions (open folder, toggle column, clear
/// cache, about) are raised as events for the host window to
/// subscribe to. This keeps the title bar reusable in a future
/// shell view without coupling it to the main viewer's services.
///
/// Migration note (P1): the public properties below (BtnOpen,
/// MenuPlugins, etc.) replace the named-XAML-element access that
/// MainWindow used to do directly. The existing partial-class
/// controllers in MainWindow now access these via the
/// <c>TitleBar.BtnXxx</c> pattern. The next refactor (P2 VMs)
/// will replace direct property access with command bindings.
/// </summary>
public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
    }

    // Public properties for MainWindow access
    public System.Windows.Controls.Border TitleBarAreaRef => TitleBarArea;
    public System.Windows.Controls.Button BtnOpenRef => BtnOpen;
    public System.Windows.Controls.Button BtnToggleTreeRef => BtnToggleTree;
    public System.Windows.Controls.Button BtnToggleThumbRef => BtnToggleThumb;
    public System.Windows.Controls.Button BtnMenuRef => BtnMenu;
    public System.Windows.Controls.Button BtnMinimizeRef => BtnMinimize;
    public System.Windows.Controls.Button BtnMaximizeRef => BtnMaximize;
    public System.Windows.Controls.Button BtnCloseRef => BtnClose;
    public Wpf.Ui.Controls.SymbolIcon MaximizeIconRef => MaximizeIcon;
    public System.Windows.Controls.MenuItem MenuPluginsRef => MenuPlugins;
    public System.Windows.Controls.MenuItem MenuAboutRef => MenuAbout;
    public System.Windows.Controls.TextBlock AboutUpdateSuffixRef => AboutUpdateSuffix;

    // ---- Self-contained: title bar drag + window controls ----

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }

        var window = Window.GetWindow(this);
        if (window == null) return;

        if (window.WindowState == WindowState.Maximized)
        {
            var point = e.GetPosition(window);
            var screenPoint = window.PointToScreen(point);
            window.ResizeMode = ResizeMode.NoResize;
            window.WindowState = WindowState.Normal;
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Square24;
            window.Left = screenPoint.X - point.X;
            window.Top = screenPoint.Y - point.Y;
            window.Width = window.RestoreBounds.Width;
            window.Height = window.RestoreBounds.Height;
            window.ResizeMode = ResizeMode.CanResize;
        }
        if (window.WindowState == WindowState.Normal)
        {
            try { window.DragMove(); } catch { /* released outside title bar — safe to ignore */ }
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w != null) w.WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        w?.Close();
    }

    private void ToggleMaximize()
    {
        var w = Window.GetWindow(this);
        if (w == null) return;
        if (w.WindowState == WindowState.Maximized)
        {
            w.WindowState = WindowState.Normal;
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Square24;
        }
        else
        {
            w.WindowState = WindowState.Maximized;
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.SquareMultiple24;
        }
    }

    // ---- Cross-component events (host window subscribes) ----

    /// <summary>Raised when the user clicks the 打开 button.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised when the user clicks the toggle-tree button.</summary>
    public event EventHandler? ToggleTreeRequested;

    /// <summary>Raised when the user clicks the toggle-thumb button.</summary>
    public event EventHandler? ToggleThumbRequested;

    /// <summary>Raised when the user clicks the overflow menu button
    /// (host repositions the ContextMenu relative to the button).</summary>
    public event EventHandler? MenuRequested;

    /// <summary>Raised when the user chooses 缩略图缓存 from the menu.</summary>
    public event EventHandler? ClearCacheRequested;

    /// <summary>Raised when the user chooses 最近访问记录 from the menu.</summary>
    public event EventHandler? ClearRecentRequested;

    /// <summary>Raised when the user clicks the 关于 menu item.</summary>
    public event EventHandler? AboutRequested;

    private void BtnOpen_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
    private void BtnToggleTree_Click(object sender, RoutedEventArgs e) => ToggleTreeRequested?.Invoke(this, EventArgs.Empty);
    private void BtnToggleThumb_Click(object sender, RoutedEventArgs e) => ToggleThumbRequested?.Invoke(this, EventArgs.Empty);
    private void BtnMenu_Click(object sender, RoutedEventArgs e) => MenuRequested?.Invoke(this, EventArgs.Empty);
    private void BtnClearCache_Click(object sender, RoutedEventArgs e) => ClearCacheRequested?.Invoke(this, EventArgs.Empty);
    private void BtnClearRecent_Click(object sender, RoutedEventArgs e) => ClearRecentRequested?.Invoke(this, EventArgs.Empty);
    private void About_Click(object sender, RoutedEventArgs e) => AboutRequested?.Invoke(this, EventArgs.Empty);
}