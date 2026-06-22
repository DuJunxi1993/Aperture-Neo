using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ApertureNeo.Models;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State for the thumbnail panel
/// (<see cref="ApertureNeo.Views.ThumbnailPanelView"/>).
///
/// Owns:
///   - ItemsSource (bound to NavigationService.Items)
///   - SelectedItem (bound to NavigationService.Current)
///   - IsEmpty (computed from Count == 0 — drives the
///     "此文件夹没有图片" empty-state overlay's Visibility)
///   - ThumbEmpty visibility (a derived boolean)
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
        _navigation.CurrentImageChanged += _ => OnPropertyChanged(nameof(SelectedItem));
    }

    public IReadOnlyList<ImageItem> Items => _navigation.Items;
    public ImageItem? SelectedItem => _navigation.Current;

    public bool IsEmpty => _navigation.Count == 0;
}