using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// One row of the Settings → Plugins panel. Backs the
/// status dot + name + description + checkbox row the
/// <c>SettingsWindow</c> renders for every discovered
/// plugin. Holds a back-reference to its <see cref="PluginInfo"/>
/// for activate/deactivate calls, plus a derived
/// <see cref="StatusBrush"/> the XAML binds to.
///
/// All bound properties (Name, Description, StatusBrush,
/// IsAvailable, IsEnabled) implement INotifyPropertyChanged so
/// the ItemsControl DataTemplate refreshes correctly when the
/// underlying plugin state changes (e.g. OCR warms up its
/// ONNX models — Status transitions Disabled → Enabled).
/// </summary>
public sealed class PluginToggleViewModel : INotifyPropertyChanged
{
    private readonly PluginInfo _info;
    private readonly ITheme _theme;
    private readonly PluginShellViewModel _pluginShell;
    private bool _isEnabled;
    private PluginStatus _status;

    public PluginToggleViewModel(PluginInfo info, bool initiallyEnabled,
        ITheme theme, PluginShellViewModel pluginShell)
    {
        _info = info;
        _theme = theme;
        _pluginShell = pluginShell;
        _isEnabled = initiallyEnabled;
        _status = info.Instance.Status;
    }

    public string Id => _info.Name;

    public string Name
    {
        get => _info.Name;
    }

    public string Description
    {
        get => _info.Description;
    }

    public PluginStatus Status
    {
        get => _status;
    }

    /// <summary>True if the plugin can't run (e.g. missing
    /// model files). The checkbox is greyed out when false
    /// so the user can see what's installed but can't toggle
    /// it on until the missing files are restored.</summary>
    public bool IsAvailable => _status != PluginStatus.Unavailable;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            // Persist + (de)activate the plugin through the
            // shell. PluginShell.SetPluginEnabled writes the
            // settings.json entry and calls
            // PluginLoader.Activate/Deactivate, so the next
            // startup will restore this state automatically.
            _pluginShell.SetPluginEnabled(_info, value);
        }
    }

    /// <summary>Brush for the 8x8 status dot:
    /// <list type="bullet">
    ///   <item><see cref="PluginStatus.Enabled"/> → <see cref="ITheme.StatusGreen"/></item>
    ///   <item><see cref="PluginStatus.Unavailable"/> → <see cref="ITheme.StatusRed"/></item>
    ///   <item><see cref="PluginStatus.Disabled"/> → <see cref="ITheme.TextTertiary"/> (gray)</item>
    /// </list>
    /// </summary>
    public Brush StatusBrush => _status switch
    {
        PluginStatus.Enabled => _theme.StatusGreen,
        PluginStatus.Unavailable => _theme.StatusRed,
        _ => _theme.TextTertiary,
    };

    /// <summary>Returns the underlying <see cref="PluginInfo"/>.
    /// SettingsViewModel uses this when toggling to call
    /// <see cref="PluginShellViewModel.SetPluginEnabled"/>.</summary>
    public PluginInfo Info => _info;

    /// <summary>
    /// Refresh the Status from the underlying plugin instance
    /// and fire PropertyChanged for Status / StatusBrush /
    /// IsAvailable. Called by SettingsViewModel on
    /// <see cref="System.Collections.Specialized.INotifyCollectionChanged.CollectionChanged"/>
    /// to pick up plugins that finish their background warmup
    /// after the Settings panel was opened.
    /// </summary>
    public void RefreshStatus()
    {
        var newStatus = _info.Instance.Status;
        if (newStatus == _status) return;
        _status = newStatus;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(IsAvailable));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}