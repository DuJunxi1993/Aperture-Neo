using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// 44px title bar extracted from MainWindow.xaml. Owns its own
/// drag-to-move, double-click-to-maximize, and window-control
/// buttons (min/max/close) — all of which are self-contained
/// state changes that don't leave the title bar.
///
/// P2 migration: the View now binds to
/// <see cref="TitleBarViewModel"/> via DataContext (set in the
/// ctor from the DI container). Open / toggle-tree / toggle-thumb
/// / clear-cache / clear-recent / about are all commands on the
/// VM; the View's XAML uses <c>Command="{Binding XxxCommand}"</c>
/// and no longer has Click handler attributes for those buttons.
/// Window-state changes (Min/Max/Close, drag-to-move) stay in
/// the View because they touch the parent Window's HWND state.
///
/// The View also still exposes a few element references
/// (<see cref="MaximizeIconRef"/>, <see cref="MenuPluginsRef"/>,
/// etc.) for the host MainWindow's IPluginContext + update-suffix
/// event hookups — those are pure UI plumbing, not business
/// logic, and stay in the view layer per MVVM.
/// </summary>
public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
        // DI lookup. WPF instantiates this control via the
        // parameterless ctor; the VM is per-window (Transient),
        // and the host MainWindow's window-state commands reach
        // the VM via the standard ICommand pattern.
        DataContext = AppHost.Services?.GetService<TitleBarViewModel>();
    }

    public Border TitleBarAreaRef => TitleBarArea;
    public System.Windows.Controls.Button BtnOpenRef => null; // BtnOpen was removed in P2 (command-bound).
    public System.Windows.Controls.Button BtnMinimizeRef => BtnMinimize;
    public System.Windows.Controls.Button BtnMaximizeRef => BtnMaximize;
    public System.Windows.Controls.Button BtnCloseRef => BtnClose;
    public Wpf.Ui.Controls.SymbolIcon MaximizeIconRef => MaximizeIcon;
    public System.Windows.Controls.MenuItem MenuPluginsRef => MenuPlugins;
    public System.Windows.Controls.MenuItem MenuAboutRef => MenuAbout;
    public System.Windows.Controls.TextBlock AboutUpdateSuffixRef => AboutUpdateSuffix;

    // ---- Self-contained: title bar drag + window controls ----
    // These touch the parent Window's HWND state, not the VM.
    // The window commands are deliberately NOT on the VM because
    // they require a Window reference (the drag-move) that isn't
    // safe to inject as a service.

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

    // BtnMenu is special: the BtnMenu's ContextMenu is defined in
    // XAML and its menu items bind to VM commands. BtnMenu itself
    // doesn't have a command — clicking it opens the ContextMenu
    // (anchored below the button) via this click handler. Pure UI.
    private void BtnMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.ContextMenu != null)
        {
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            b.ContextMenu.IsOpen = true;
        }
    }
}