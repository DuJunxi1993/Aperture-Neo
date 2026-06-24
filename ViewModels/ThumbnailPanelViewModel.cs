using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Models;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State + commands for the thumbnail panel
/// (<see cref="ApertureNeo.Views.ThumbnailPanelView"/>).
///
/// Owns:
///   - ItemsSource (bound to NavigationService.Items via the
///     <see cref="Items"/> property + PropertyChanged)
///   - SelectedItem (bound to NavigationService.Current via
///     <see cref="SelectedItem"/>)
///   - IsEmpty (computed from Count == 0 — drives the
///     "此文件夹没有图片" empty-state overlay's Visibility)
///   - ThumbClicked command: navigates to the clicked thumb's
///     file path.
///   - RequestScrollIntoView event: raised on every navigation
///     change so the View can call ScrollSelectedIntoView on
///     the ThumbnailGrid.
///
/// P2: the VM owns the thumb-click navigation now; previously
/// it lived in InfoPopoverController.OnThumbClicked. The
/// scroll-into-view used to be triggered from
/// InfoPopoverController.OnCurrentImageChanged (alongside the
/// window title update + viewer context-menu close). The
/// scroll-into-view portion moved here because it concerns
/// the thumbnail panel; the title + context-menu portions
/// stayed in MainWindow.xaml.cs (window-level state).
/// </summary>
public partial class ThumbnailPanelViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public ThumbnailPanelViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        _navigation.CollectionChanged += () =>
        {
            OnPropertyChanged(nameof(Items));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(SelectedItem));
        };
        _navigation.CurrentImageChanged += OnCurrentImageChanged;
    }

    private void OnCurrentImageChanged(ImageItem? item)
    {
        OnPropertyChanged(nameof(SelectedItem));
        // The View subscribes to this event and calls
        // ThumbGrid.ScrollSelectedIntoView(); the VM doesn't
        // reach the grid directly.
        RequestScrollIntoView?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<ImageItem> Items => _navigation.Items;
    public ImageItem? SelectedItem
    {
        get => _navigation.Current;
        set
        {
            // P1 fix: setter required for TwoWay binding.
            // The binding writes the target's SelectedItem
            // back to the source on every change; without a
            // setter the binding fails at activation
            // ("read-only property 'SelectedItem' cannot be
            // TwoWay-bound"). Forward to NavigationService
            // so the underlying state matches what the user
            // clicked. The value==current guard prevents
            // re-entrant NavigateTo calls when the binding
            // round-trips the same value back (binding →
            // setter → NavigateTo → CurrentImageChanged →
            // PropertyChanged → binding → setter is a no-op).
            if (value != null && value != _navigation.Current)
                _navigation.NavigateTo(value.FilePath);
        }
    }

    public bool IsEmpty => _navigation.Count == 0;

    /// <summary>Raised on every navigation change. The View
    /// (ThumbnailPanelView) subscribes and calls
    /// ThumbGrid.ScrollSelectedIntoView() so the current
    /// image stays visible as the user navigates.</summary>
    public event EventHandler? RequestScrollIntoView;

    /// <summary>Bound to ThumbnailGrid.ItemClicked. Navigates
    /// the main viewer to the clicked item.</summary>
    [RelayCommand]
    private void ThumbClicked(ImageItem? item)
    {
        if (item == null) return;
        _navigation.NavigateTo(item.FilePath);
    }
}