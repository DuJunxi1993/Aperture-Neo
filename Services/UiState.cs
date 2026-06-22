using System.ComponentModel;
using System.Runtime.CompilerServices;
using ApertureNeo.Models;

namespace ApertureNeo.Services;

/// <summary>
/// Concrete <see cref="IUiState"/>. Pure POCO with manual
/// PropertyChanged notifications; doesn't use the
/// CommunityToolkit.Mvvm source generators because the type
/// is registered as a singleton and shared across many VMs
/// (source-generated partial classes still work for that,
/// but the hand-rolled setter is just as concise and avoids
/// an extra code-gen dependency on the project's csproj).
/// </summary>
public sealed class UiState : IUiState
{
    private bool _isFullscreen;
    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => Set(ref _isFullscreen, value);
    }

    private bool _isSlideshowRunning;
    public bool IsSlideshowRunning
    {
        get => _isSlideshowRunning;
        set => Set(ref _isSlideshowRunning, value);
    }

    private bool _isTreeVisible = true;
    public bool IsTreeVisible
    {
        get => _isTreeVisible;
        set => Set(ref _isTreeVisible, value);
    }

    private bool _isThumbVisible = true;
    public bool IsThumbVisible
    {
        get => _isThumbVisible;
        set => Set(ref _isThumbVisible, value);
    }

    private ImageItem? _currentImage;
    public ImageItem? CurrentImage
    {
        get => _currentImage;
        set => Set(ref _currentImage, value);
    }

    private int _currentImageIndex = -1;
    public int CurrentImageIndex
    {
        get => _currentImageIndex;
        set => Set(ref _currentImageIndex, value);
    }

    private int _imageCount;
    public int ImageCount
    {
        get => _imageCount;
        set => Set(ref _imageCount, value);
    }

    private double _currentZoom = 1.0;
    public double CurrentZoom
    {
        get => _currentZoom;
        set => Set(ref _currentZoom, value);
    }

    private bool _isUpdateAvailable;
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => Set(ref _isUpdateAvailable, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}