using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ApertureNeo.Services;

namespace ApertureNeo.ViewModels;

/// <summary>
/// View-model for one row of the shortcut list in the
/// settings panel. Holds the current display string, exposes
/// "start recording" / "reset" / "clear" commands, and surfaces
/// any conflict detected after a fresh keystroke.
///
/// Recording protocol (decision E1 — click-to-record):
/// <list type="number">
///   <item>User clicks [录制] → <see cref="IsRecording"/> flips
///         to true; the settings window installs a temporary
///         PreviewKeyDown handler.</item>
///   <item>User presses any key combination → handler captures
///         the gesture, runs conflict detection, and either
///         commits (writes to <see cref="ISettingsStore"/> +
///         updates <see cref="IShortcutService"/>) or shows
///         <see cref="ConflictMessage"/>.</item>
///   <item>Esc cancels; the temporary handler is removed.</item>
/// </list>
///
/// The handler is attached by the SettingsWindow code-behind
/// (not here) because key capture needs a UI element with
/// keyboard focus — DataContext alone can't subscribe to
/// window-level key events.
/// </summary>
public sealed class ShortcutRecorder : INotifyPropertyChanged
{
    private readonly string _id;
    private readonly ISettingsStore _settings;
    private readonly IShortcutService _shortcutService;
    private string _currentDisplay;
    private bool _isRecording;
    private string? _conflictMessage;

    public ShortcutRecorder(string id, string label, string gesture,
        ISettingsStore settings, IShortcutService shortcutService)
    {
        _id = id;
        _settings = settings;
        _shortcutService = shortcutService;
        Label = label;
        _currentDisplay = gesture;
        StartRecordingCommand = new RelayCommand(_ => IsRecording = true);
        ResetCommand = new RelayCommand(_ => ResetToDefault());
        ClearCommand = new RelayCommand(_ => Clear());
    }

    public string Label { get; }

    public string CurrentDisplay
    {
        get => _currentDisplay;
        set { if (_currentDisplay != value) { _currentDisplay = value; OnPropertyChanged(); } }
    }

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (_isRecording == value) return;
            _isRecording = value;
            OnPropertyChanged();
            // Clear any stale conflict message on entering/exiting
            // recording mode.
            ConflictMessage = null;
            // Notify the SettingsWindow so it can install/remove
            // its key-capture hook.
            if (value) RecordingStarted?.Invoke(this);
        }
    }

    public string? ConflictMessage
    {
        get => _conflictMessage;
        set { if (_conflictMessage != value) { _conflictMessage = value; OnPropertyChanged(); } }
    }

    /// <summary>Raised when this recorder transitions into the
    /// "waiting for keystroke" state. The SettingsWindow
    /// subscribes to capture the next PreviewKeyDown.</summary>
    public event Action<ShortcutRecorder>? RecordingStarted;

    public ICommand StartRecordingCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ClearCommand { get; }

    /// <summary>Id used by the SettingsWindow to find this row
    /// from the captured keystroke.</summary>
    public string Id => _id;

    /// <summary>
    /// Commit a freshly-captured keystroke. Called by the
    /// SettingsWindow's PreviewKeyDown handler when this
    /// recorder is in <see cref="IsRecording"/> mode.
    /// Returns true if the gesture was accepted (no conflict),
    /// false if it conflicted with another binding (the caller
    /// leaves IsRecording = true so the user can retry).
    /// </summary>
    public bool TryCommit(string gestureString)
    {
        if (string.IsNullOrWhiteSpace(gestureString)) return false;
        if (HasConflict(_id, gestureString))
        {
            ConflictMessage = $"与其它快捷键冲突 ({gestureString})";
            return false;
        }

        _settings.SetShortcut(_id, gestureString);
        CurrentDisplay = gestureString;

        // Update the live binding too — the running hotkey
        // should pick up the new combo immediately. We do
        // NOT call Register() if the hotkey is not yet active,
        // because Register requires a real callback (the
        // "rebind callback lives in plugin" comment from the
        // previous incarnation was wrong: passing an empty
        // () => { } here clobbered the real registration
        // with a no-op). Instead, the plugin (e.g.
        // ScreenshotPlugin) is the sole owner of the
        // callback and re-registers itself on Activate using
        // the gesture now persisted in settings. If the user
        // enables the plugin via the Settings → Plugins
        // panel, the new gesture takes effect immediately
        // (SetPluginEnabled calls PluginLoader.Activate which
        // calls TryRegisterShortcut which reads settings).
        try
        {
            var converter = new System.Windows.Input.KeyGestureConverter();
            var gesture = (System.Windows.Input.KeyGesture?)converter.ConvertFromString(gestureString);
            if (gesture != null
                && _shortcutService is IShortcutService2 extended
                && extended.IsActive(_id))
            {
                _shortcutService.Update(_id, gesture);
            }
        }
        catch { /* parse error — settings already saved; user can fix */ }

        IsRecording = false;
        return true;
    }

    private void ResetToDefault()
    {
        var defaults = SettingsStore.DefaultShortcuts;
        if (!defaults.TryGetValue(_id, out var gesture)) return;
        _settings.SetShortcut(_id, gesture);
        CurrentDisplay = gesture;
        // Apply live if the plugin has already registered
        // the hotkey. We deliberately do NOT call Register
        // for the not-yet-active case (see TryCommit for
        // why — passing an empty callback would break the
        // binding).
        try
        {
            var converter = new System.Windows.Input.KeyGestureConverter();
            var g = (System.Windows.Input.KeyGesture?)converter.ConvertFromString(gesture);
            if (g != null
                && _shortcutService is IShortcutService2 ext
                && ext.IsActive(_id))
            {
                _shortcutService.Update(_id, g);
            }
        }
        catch { }
    }

    private void Clear()
    {
        // Clear the user's override; restore the default
        // display. We don't Unregister the live hotkey —
        // that would tear down the plugin's real callback
        // registration, and if the user re-records a
        // shortcut immediately afterwards the recorder
        // can't re-create the binding (it doesn't own the
        // callback). Instead, push the default gesture
        // back into the live binding so the row's behaviour
        // matches its display.
        _settings.SetShortcut(_id, null);
        if (SettingsStore.DefaultShortcuts.TryGetValue(_id, out var d))
        {
            CurrentDisplay = d;
            try
            {
                var converter = new System.Windows.Input.KeyGestureConverter();
                var g = (System.Windows.Input.KeyGesture?)converter.ConvertFromString(d);
                if (g != null
                    && _shortcutService is IShortcutService2 ext
                    && ext.IsActive(_id))
                {
                    _shortcutService.Update(_id, g);
                }
            }
            catch { }
        }
        else
        {
            CurrentDisplay = "(未设置)";
            _shortcutService.Unregister(_id);
        }
    }

    private bool HasConflict(string excludeId, string gestureString)
    {
        foreach (var (id, gesture) in _settings.GetShortcuts())
        {
            if (id == excludeId) continue;
            if (string.Equals(gesture, gestureString, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}