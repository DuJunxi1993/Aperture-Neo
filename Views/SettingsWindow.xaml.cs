using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ApertureNeo.Services;
using ApertureNeo.ViewModels;

namespace ApertureNeo.Views;

/// <summary>
/// Stage 8 settings panel. Five categories (shortcuts, OCR,
/// screenshot, plugins, general) sharing one window via
/// sidebar navigation. Hosts the click-to-record key capture
/// (decision E1 — Stage 9): the active recorder subscribes
/// to PreviewKeyDown on this window; on capture the gesture
/// string is built from the WPF Key/Modifier state and routed
/// to the recorder's TryCommit.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private ShortcutRecorder? _activeRecorder;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        // Wire each recorder's RecordingStarted → our BeginRecording
        // handler. The settings VM already constructed the recorders
        // (in its ctor); we just connect the callback now.
        foreach (var recorder in _vm.Shortcuts)
            recorder.RecordingStarted += OnRecorderStarted;

        NavList.SelectionChanged += (_, _) =>
        {
            var tag = (NavList.SelectedItem as ListBoxItem)?.Tag as string;
            ShortcutsPanel.Visibility = tag == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
            OcrPanel.Visibility = tag == "ocr" ? Visibility.Visible : Visibility.Collapsed;
            ScreenshotPanel.Visibility = tag == "screenshot" ? Visibility.Visible : Visibility.Collapsed;
            PluginsPanel.Visibility = tag == "plugins" ? Visibility.Visible : Visibility.Collapsed;
            GeneralPanel.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        };

        // Cleanly exit any active recording on window close so the
        // recorder's IsRecording doesn't get stuck.
        Closing += (_, _) =>
        {
            foreach (var recorder in _vm.Shortcuts)
                recorder.RecordingStarted -= OnRecorderStarted;
            _activeRecorder = null;
        };
    }

    private void OnRecorderStarted(ShortcutRecorder recorder)
    {
        BeginRecording(recorder);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    // Drag-move for the chrome-less title bar.
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is ButtonBase) return;
        try { DragMove(); } catch { /* released outside — fine */ }
    }

    /// <summary>
    /// Window-level key capture (decision E1 — click-to-record).
    /// Active only when one of the shortcut rows is in
    /// IsRecording mode. Listens for the next PreviewKeyDown
    /// and routes it to the active recorder.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Honor Escape first — always cancels regardless of mode.
        if (e.Key == Key.Escape && _activeRecorder != null)
        {
            _activeRecorder.IsRecording = false;
            _activeRecorder = null;
            e.Handled = true;
            return;
        }

        if (_activeRecorder == null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        // Ignore modifier-only presses (wait for the actual key).
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt  || e.Key == Key.RightAlt  ||
            e.Key == Key.LeftShift|| e.Key == Key.RightShift||
            e.Key == Key.LWin     || e.Key == Key.RWin)
        {
            return;
        }

        var gesture = ToGestureString(e.Key);
        if (gesture == null)
        {
            // Unrecognised key — cancel so the user isn't stuck.
            _activeRecorder.IsRecording = false;
            _activeRecorder = null;
            e.Handled = true;
            return;
        }

        var ok = _activeRecorder.TryCommit(gesture);
        if (ok)
            _activeRecorder = null;
        e.Handled = true;
    }

    /// <summary>Called by ShortcutRecorder when its
    /// StartRecordingCommand fires. Replaces the previous
    /// active recorder (if any) and re-shows the
    /// RecordingState visual.</summary>
    public void BeginRecording(ShortcutRecorder recorder)
    {
        // Cancel any other row's recording state first.
        if (_activeRecorder != null && _activeRecorder != recorder)
            _activeRecorder.IsRecording = false;

        _activeRecorder = recorder;
        Focus();   // make sure key events reach this window
    }

    /// <summary>
    /// Build a WPF-style gesture string ("Ctrl+Alt+A") from
    /// the captured KeyEventArgs. The string format matches
    /// KeyGestureConverter's input — same one the settings
    /// file uses.
    /// </summary>
    private static string? ToGestureString(Key key)
    {
        // Reject keys that don't make sense as hotkey bindings.
        if (key == Key.None || key == Key.DeadCharProcessed ||
            key == Key.Print || key == Key.Sleep)
            return null;

        var parts = new System.Collections.Generic.List<string>(4);
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");

        // Use Key.ToString() so the gesture string stays
        // canonical ("A" not "65"). KeyGestureConverter parses
        // both, but the canonical form is friendlier when the
        // user reads settings.json.
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}