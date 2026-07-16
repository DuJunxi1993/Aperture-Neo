using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State for one edge-nav button (left / right). Owns its
/// Visibility (driven by <see cref="IUiState.IsFullscreen"/>)
/// and an Opacity animation that's reset on every CursorMove
/// event (host-driven). The Direction is a per-instance
/// property set by the View (via the existing DependencyProperty).
/// </summary>
public partial class EdgeNavViewModel : ObservableObject
{
    private readonly IUiState _uiState;

    public EdgeNavViewModel(IUiState uiState)
    {
        _uiState = uiState;
        _uiState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IUiState.IsFullscreen))
                IsVisible = _uiState.IsFullscreen;
        };
    }

    [ObservableProperty]
    private bool _isVisible;

    [RelayCommand]
    private void Navigate()
    {
        NavigateRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the user clicks the button. Host
    /// routes to MovePrevious (left) or MoveNext (right).</summary>
    public event EventHandler? NavigateRequested;
}