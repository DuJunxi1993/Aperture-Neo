using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// Backs the <c>Views.SettingsWindow</c>. Holds one
/// <see cref="ShortcutRecorder"/> per registered shortcut
/// (data-bound to the shortcut list in the UI) and the
/// non-shortcut settings (auto-start, close-to-tray, OCR
/// toggle, default save directory). Owns no disposable
/// resources — settings writes go through the injected
/// <see cref="ISettingsStore"/>, which already debounces.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore _settings;
    private readonly IShortcutService _shortcutService;
    private readonly PluginShellViewModel _pluginShell;
    private readonly ITheme _theme;

    public SettingsViewModel(ISettingsStore settings,
        IShortcutService shortcutService,
        PluginShellViewModel pluginShell,
        ITheme theme)
    {
        _settings = settings;
        _shortcutService = shortcutService;
        _pluginShell = pluginShell;
        _theme = theme;

        AutoStart = _settings.AutoStart;
        CloseToTray = _settings.CloseToTray;
        EditorOcrShowWindow = _settings.EditorOcrShowWindow;
        DefaultSaveDir = _settings.DefaultScreenshotSaveDirectory ?? string.Empty;

        Shortcuts = new ObservableCollection<ShortcutRecorder>();
        RebuildShortcuts();

        Plugins = new ObservableCollection<PluginToggleViewModel>();
        RebuildPlugins();

        // Stage D2: subscribe to the plugin shell's
        // AvailablePlugins collection. The shell's
        // SetAvailablePlugins is called once after
        // PluginLoader.Discover; if the user opens Settings
        // before that line runs (e.g. the first-run dialog is
        // still up), RebuildPlugins sees an empty list and
        // the panel renders blank. Subscribing to
        // CollectionChanged ensures the panel re-fills
        // automatically when the discovery completes.
        ((INotifyCollectionChanged)_pluginShell.AvailablePlugins).CollectionChanged
            += (_, _) =>
            {
                // CollectionChanged can fire on any thread that
                // mutates the collection (e.g. SetAvailablePlugins
                // on a worker thread). Marshal the rebuild to the
                // WPF UI thread so ItemsControl's CollectionChanged
                // handler runs on the dispatcher that owns it.
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    new Action(RebuildPlugins));
            };

        // AutoStart / CloseToTray / EditorOcrShowWindow write
        // through SettingsStore directly in their property
        // setters (TwoWay bindings in the XAML go through them),
        // so no separate ToggleXxx commands are needed.
        BrowseSaveDirCommand = new RelayCommand(_ => BrowseSaveDir());
        ResetAllShortcutsCommand = new RelayCommand(_ => ResetAllShortcuts());
    }

    public ObservableCollection<ShortcutRecorder> Shortcuts { get; }
    public ObservableCollection<PluginToggleViewModel> Plugins { get; }

    private bool _autoStart;
    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (_autoStart == value) return;
            _autoStart = value;
            // Write through to SettingsStore so the change is
            // debounced-saved to settings.json. The XAML binds
            // IsChecked TwoWay to this property, so the setter
            // runs on every toggle — without the SettingsStore
            // write below, the toggle would only update the
            // in-memory field and the value would reset on
            // next launch.
            _settings.AutoStart = value;
            // Mirror the toggle to the registry so the OS
            // actually auto-launches the app next boot. Wrapped
            // in try/catch because the registry write can fail
            // (HKCU\...\Run requires write access; some
            // enterprise policies disable it). On failure we
            // roll the toggle back so the UI reflects the real
            // state and surface a warning.
            try
            {
                AutoStartService.SetEnabled(value);
            }
            catch (Exception ex)
            {
                _autoStart = !value;
                _settings.AutoStart = _autoStart;
                System.Windows.MessageBox.Show(
                    $"{ex.Message}\n\n(可以尝试以管理员身份运行 Aperture Neo)",
                    "自动启动", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            OnPropertyChanged();
        }
    }

    private bool _closeToTray;
    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (_closeToTray == value) return;
            _closeToTray = value;
            _settings.CloseToTray = value;
            OnPropertyChanged();
        }
    }

    private bool _editorOcrShowWindow;
    public bool EditorOcrShowWindow
    {
        get => _editorOcrShowWindow;
        set
        {
            if (_editorOcrShowWindow == value) return;
            _editorOcrShowWindow = value;
            _settings.EditorOcrShowWindow = value;
            OnPropertyChanged();
        }
    }

    private string _defaultSaveDir = string.Empty;
    public string DefaultSaveDir
    {
        get => _defaultSaveDir;
        set { if (_defaultSaveDir != value) { _defaultSaveDir = value; OnPropertyChanged(); } }
    }

    public ICommand BrowseSaveDirCommand { get; }
    public ICommand ResetAllShortcutsCommand { get; }

    /// <summary>Reset every shortcut binding to the canonical
    /// default and re-register on the OS. Called from the
    /// settings panel's "重置全部" button.</summary>
    public void ResetAllShortcuts()
    {
        _settings.ResetShortcuts();
        RebuildShortcuts();
    }

/// <summary>Refresh the Plugins list from the plugin
/// shell's <see cref="PluginShellViewModel.AvailablePlugins"/>.
/// Called once at construction — plugins are discovered
/// once at app startup and don't change.</summary>
private void RebuildPlugins()
{
    var available = _pluginShell.AvailablePlugins;
    ApertureNeo.Services.DebugLog.Write(
        "SettingsViewModel",
        $"RebuildPlugins: AvailablePlugins.Count={available.Count}, current Plugins.Count={Plugins.Count}");

    // Clear + Add. ObservableCollection<T> raises Reset on
    // Clear and Add on each Add; ItemsControl handles them
    // atomically per render frame. We don't replace the
    // collection reference because the binding source is the
    // PUBLIC Plugins property — replacing the field would
    // require a manual PropertyChanged and risks losing
    // any in-progress render.
    Plugins.Clear();
    foreach (var info in available)
    {
        Plugins.Add(new PluginToggleViewModel(
            info,
            initiallyEnabled: _settings.IsPluginEnabled(info.Name),
            theme: _theme,
            pluginShell: _pluginShell));
    }
    ApertureNeo.Services.DebugLog.Write(
        "SettingsViewModel",
        $"RebuildPlugins: end, Plugins.Count={Plugins.Count}");

    // Surface the discovery state on the panel itself so the
    // user can see *why* it might be empty (e.g. plugins
    // directory missing) without having to dig through the
    // debug log.
    PluginDiagnosticText = available.Count == 0
        ? "(未发现插件 — 检查 bin/Plugins 目录是否包含 *.dll 文件)"
        : $"已发现 {available.Count} 个插件";
    OnPropertyChanged(nameof(PluginDiagnosticText));
}

    private string _pluginDiagnosticText = string.Empty;
    public string PluginDiagnosticText
    {
        get => _pluginDiagnosticText;
        set { if (_pluginDiagnosticText != value) { _pluginDiagnosticText = value; OnPropertyChanged(); } }
    }

    private void RebuildShortcuts()
    {
        Shortcuts.Clear();
        var current = _settings.GetShortcuts();
        // Iterate the canonical default ordering so the
        // list is stable across sessions even when the user
        // has custom overrides.
        foreach (var (id, defaultGesture) in SettingsStore.DefaultShortcuts)
        {
            var gesture = current.TryGetValue(id, out var v) ? v : defaultGesture;
            Shortcuts.Add(new ShortcutRecorder(id, LabelFor(id), gesture, _settings, _shortcutService));
        }
    }

    private static string LabelFor(string id) => id switch
    {
        "ScreenshotPlugin.CaptureArea" => "截图选区",
        "ScreenshotPlugin.CaptureOcr" => "OCR 选区",
        "ScreenshotPlugin.CaptureFullscreen" => "全屏截图",
        _ => id,
    };

    /// <summary>Open a folder-picker dialog to choose the
    /// default screenshot save directory.</summary>
    private void BrowseSaveDir()
    {
        // Use the Microsoft.Win32 OpenFolderDialog (available
        // on net10.0-windows). No external WinForms FolderBrowserDialog
        // dependency needed.
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择默认截图保存目录",
            InitialDirectory = string.IsNullOrEmpty(DefaultSaveDir) ? null : DefaultSaveDir,
        };
        if (dlg.ShowDialog() == true)
        {
            DefaultSaveDir = dlg.FolderName;
            _settings.DefaultScreenshotSaveDirectory = dlg.FolderName;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}