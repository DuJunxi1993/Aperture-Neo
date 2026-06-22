using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Controls;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// Thumbnail panel: hosts <see cref="ThumbnailGrid"/> + the
/// empty-state overlay. P2: binds to
/// <see cref="ThumbnailPanelViewModel"/> via DataContext.
/// ItemsSource + SelectedItem + IsEmpty are VM-driven.
///
/// The host (MainWindow) reads <see cref="ThumbGridRef"/> to
/// wire the per-item click + the inner ScrollViewer's
/// ScrollChanged event after the template has applied.
/// </summary>
public partial class ThumbnailPanelView : UserControl
{
    public ThumbnailPanelView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<ThumbnailPanelViewModel>();
    }

    public Border ThumbPanelRef => ThumbPanel;
    public ThumbnailGrid ThumbGridRef => ThumbGrid;
    public StackPanel ThumbEmptyRef => ThumbEmpty;

    /// <summary>Re-raised from ThumbGrid.Loaded so the host can
    /// wire the inner ScrollViewer's ScrollChanged event after
    /// the template has applied.</summary>
    public event EventHandler? ThumbGridReady;

    private void ThumbGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ThumbGridReady?.Invoke(this, EventArgs.Empty);
    }
}