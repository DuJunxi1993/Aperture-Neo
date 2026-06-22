using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Controls;

namespace ApertureNeo.Views;

/// <summary>
/// Thumbnail panel: hosts <see cref="ThumbnailGrid"/> + the
/// empty-state overlay. Migrated from MainWindow.xaml.
///
/// The host (MainWindow) reads <see cref="ThumbGridRef"/> to
/// set ItemsSource + SelectedItem, and <see cref="ThumbEmptyRef"/>
/// to toggle the empty-state overlay's visibility based on
/// whether the current folder has images.
/// </summary>
public partial class ThumbnailPanelView : UserControl
{
    public ThumbnailPanelView()
    {
        InitializeComponent();
    }

    public Border ThumbPanelRef => ThumbPanel;
    public ThumbnailGrid ThumbGridRef => ThumbGrid;
    public StackPanel ThumbEmptyRef => ThumbEmpty;

    /// <summary>Re-raised from ThumbGrid.Loaded so the host can
    /// wire the inner ScrollViewer's ScrollChanged event after
    /// the template has applied. See InfoPopoverController
    /// (currently MainWindow) for the consumer.</summary>
    public event EventHandler? ThumbGridReady;

    private void ThumbGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ThumbGridReady?.Invoke(this, EventArgs.Empty);
    }
}