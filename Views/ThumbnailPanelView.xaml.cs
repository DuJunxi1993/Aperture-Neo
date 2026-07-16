using System;
using System.Windows;
using System.Windows.Controls;
using ApertureNeo.Controls;
using ApertureNeo.Models;
using ApertureNeo.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ApertureNeo.Views;

/// <summary>
/// Thumbnail panel: hosts <see cref="ThumbnailGrid"/> + the
/// empty-state overlay. P2: binds to
/// <see cref="ThumbnailPanelViewModel"/> via DataContext.
/// ItemsSource + SelectedItem + IsEmpty are VM-driven.
///
/// P2: the View is the bridge between the thumb grid (which
/// the VM doesn't have a reference to) and the VM. It wires
/// ThumbGrid.ItemClicked to the VM's ThumbClickedCommand,
/// and subscribes to the VM's RequestScrollIntoView event to
/// call ThumbGrid.ScrollSelectedIntoView.
///
/// The host (MainWindow) reads <see cref="ThumbGridRef"/> to
/// wire the inner ScrollViewer's ScrollChanged event after
/// the template has applied.
/// </summary>
public partial class ThumbnailPanelView : UserControl
{
    public ThumbnailPanelView()
    {
        InitializeComponent();
        DataContext = AppHost.Services?.GetService<ThumbnailPanelViewModel>();

        if (DataContext is ThumbnailPanelViewModel vm)
        {
            // P2: per-thumb navigation goes through the VM's
            // ThumbClickedCommand; the View only forwards the
            // grid's ItemClicked event into the command.
            ThumbGrid.ItemClicked += item => vm.ThumbClickedCommand.Execute(item);
            // P2: navigation triggers a scroll-into-view so the
            // current item stays visible as the user flips through
            // images; the VM raises RequestScrollIntoView and the
            // View translates it into ScrollSelectedIntoView on
            // the grid.
            vm.RequestScrollIntoView += (_, _) => ThumbGrid.ScrollSelectedIntoView();
        }
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