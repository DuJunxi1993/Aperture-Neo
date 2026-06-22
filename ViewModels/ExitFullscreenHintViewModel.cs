using System;
using System.Windows;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// State for the "退出全屏 (Esc / Ctrl+F)" pill at the top of
/// the viewer in fullscreen. The visibility / opacity animation
/// is still managed by the host (MainWindow) because the
/// animation timer + translate transform live outside the VM.
/// The VM exposes a simple <see cref="IsVisible"/> flag bound
/// from <see cref="IUiState.IsFullscreen"/>.
/// </summary>
public partial class ExitFullscreenHintViewModel : ObservableObject
{
    private readonly IUiState _uiState;

    public ExitFullscreenHintViewModel(IUiState uiState)
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
}