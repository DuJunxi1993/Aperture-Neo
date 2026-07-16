using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ApertureNeo.Helpers;

namespace ApertureNeo.Services;

/// <summary>
/// Win32 <c>RegisterHotKey</c> implementation of
/// <see cref="IShortcutService"/>. Hooks a <c>WM_HOTKEY</c>
/// listener on the main window's HWND via <c>HwndSource</c>;
/// dispatches the configured callback on the UI thread.
///
/// Slot allocation: Win32 hotkey ids are integers in the
/// <c>0x0000</c>–<c>0xBFFF</c> range. We use the upper slot
/// (<c>0xA000 + n</c>) where n is the per-service counter;
/// MOD_NOREPEAT (0x4000) is OR'd into the modifiers so a
/// held key only fires once, matching the modern
/// <c>RegisterHotKey</c> convention.
///
/// Lifecycle:
/// <list type="number">
///   <item>Construct (no Win32 calls yet — the main window's
///         HWND doesn't exist at DI-build time).</item>
///   <item><see cref="Initialize"/> — pass the main
///         <c>Window</c>; the service attaches a hook to the
///         window's HWND source. Idempotent.</item>
///   <item><see cref="Register"/> / <see cref="Update"/> —
///         user-callable; can happen before or after
///         <see cref="Initialize"/>.</item>
///   <item><see cref="Unregister"/> — called from
///         <see cref="IServiceProvider"/> shutdown paths or
///         when plugins deactivate.</item>
/// </list>
/// </summary>
public sealed class GlobalHotkeyService : IShortcutService, IShortcutService2, IDisposable
{
    private const int HotkeyIdBase = 0xA000;
    private const int ModNorepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WmHotkey = 0x0312;

    private readonly object _lock = new();

    // Active registrations: id → {Win32 id, KeyGesture, callback}.
    // The "id" key is the stable plugin-defined string; "Win32 id"
    // is the OS-level handle.
    private readonly Dictionary<string, Registration> _regs = new(StringComparer.OrdinalIgnoreCase);

    // Reverse lookup: Win32 id → stable id. Needed in the WndProc
    // hook to map a hotkey message back to the callback.
    private readonly Dictionary<int, string> _winIdToStringId = new();

    private int _nextWinId = HotkeyIdBase;

    private HwndSource? _hwndSource;
    private IntPtr _hwnd;
    private bool _disposed;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, KeyGesture> Registered
    {
        get
        {
            lock (_lock)
            {
                var snap = new Dictionary<string, KeyGesture>(_regs.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in _regs)
                    snap[kv.Key] = kv.Value.Gesture;
                return snap;
            }
        }
    }

    /// <summary>
    /// Attach the message hook to the given window. Must be
    /// called after the window has been <c>Show</c>n (the HWND
    /// is created lazily by WPF). Safe to call multiple times —
    /// the hook is only attached once per HWND.
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        if (_hwndSource != null) return;
        var helper = new WindowInteropHelper(mainWindow);
        _hwnd = helper.Handle;
        if (_hwnd == IntPtr.Zero)
        {
            // WPF hasn't created the HWND yet. Defer until the
            // window's SourceInitialized event fires.
            mainWindow.SourceInitialized += (_, _) => Initialize(mainWindow);
            return;
        }
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource.AddHook(WndProc);
    }

    /// <inheritdoc />
    public bool Register(string id, KeyGesture gesture, Action callback)
    {
        if (_disposed) return false;
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id required", nameof(id));
        if (gesture == null) throw new ArgumentNullException(nameof(gesture));
        if (callback == null) throw new ArgumentNullException(nameof(callback));

        lock (_lock)
        {
            if (_regs.ContainsKey(id)) return false;   // already registered
            if (_hwnd == IntPtr.Zero)
            {
                // Window not yet realized — can't register until
                // Initialize runs. Log it (was previously silent
                // — caller had no way to tell "combo conflict"
                // apart from "service not initialized yet").
                DebugLog.Write("GlobalHotkeyService",
                    $"Register({id}) called before Initialize() — HWND is IntPtr.Zero");
                return false;
            }
            var winId = _nextWinId++;
            var (modifiers, vk) = ToWin32(gesture);
            if (!RegisterHotKey(_hwnd, winId, modifiers, vk))
            {
                DebugLog.Write("GlobalHotkeyService",
                    $"RegisterHotKey({gesture}) failed: 0x{Marshal.GetLastWin32Error():X}");
                return false;
            }
            _regs[id] = new Registration(winId, gesture, callback);
            _winIdToStringId[winId] = id;
            return true;
        }
    }

    /// <inheritdoc />
    public void Unregister(string id)
    {
        lock (_lock)
        {
            if (!_regs.TryGetValue(id, out var reg)) return;
            if (_hwnd != IntPtr.Zero)
                UnregisterHotKey(_hwnd, reg.WinId);
            _winIdToStringId.Remove(reg.WinId);
            _regs.Remove(id);
        }
    }

    /// <inheritdoc />
    public bool Update(string id, KeyGesture gesture)
    {
        lock (_lock)
        {
            if (!_regs.TryGetValue(id, out var reg)) return false;
            // Atomically: unregister the old binding, register the new one.
            if (_hwnd != IntPtr.Zero)
                UnregisterHotKey(_hwnd, reg.WinId);
            var winId = reg.WinId;     // reuse the same Win32 id slot
            var (modifiers, vk) = ToWin32(gesture);
            if (!RegisterHotKey(_hwnd, winId, modifiers, vk))
            {
                // Roll back: re-register the old gesture.
                var (oldMods, oldVk) = ToWin32(reg.Gesture);
                RegisterHotKey(_hwnd, winId, oldMods, oldVk);
                return false;
            }
            _regs[id] = reg.WithGesture(gesture);
            return true;
        }
    }

    /// <inheritdoc />
    public bool TryGetGesture(string id, out KeyGesture gesture)
    {
        lock (_lock)
        {
            if (_regs.TryGetValue(id, out var reg))
            {
                gesture = reg.Gesture;
                return true;
            }
            gesture = null!;
            return false;
        }
    }

    /// <summary>
    /// True if the given id is currently registered AND the OS
    /// accepted the binding (registration may have succeeded in
    /// our dictionary but failed at the Win32 layer for an
    /// older id — not currently possible, but kept for parity
    /// with future monitoring needs).
    /// </summary>
    public bool IsActive(string id) => TryGetGesture(id, out _);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            foreach (var reg in _regs.Values)
            {
                if (_hwnd != IntPtr.Zero)
                    UnregisterHotKey(_hwnd, reg.WinId);
            }
            _regs.Clear();
            _winIdToStringId.Clear();
        }
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    // ---- internals ----

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;

        var winId = wParam.ToInt32();
        Action? callback = null;
        lock (_lock)
        {
            if (_winIdToStringId.TryGetValue(winId, out var id) &&
                _regs.TryGetValue(id, out var reg))
            {
                callback = reg.Callback;
            }
        }
        if (callback != null)
        {
            try { callback(); }
            catch (Exception ex)
            {
                DebugLog.Write("GlobalHotkeyService",
                    $"callback for winId {winId:X} threw: {ex.Message}");
            }
        }
        return IntPtr.Zero;
    }

    private static (uint Modifiers, uint Vk) ToWin32(KeyGesture gesture)
    {
        uint mods = ModNorepeat;
        if ((gesture.Modifiers & ModifierKeys.Control) != 0) mods |= 0x0002;   // MOD_CONTROL
        if ((gesture.Modifiers & ModifierKeys.Alt) != 0) mods |= 0x0001;       // MOD_ALT
        if ((gesture.Modifiers & ModifierKeys.Shift) != 0) mods |= 0x0004;     // MOD_SHIFT
        if ((gesture.Modifiers & ModifierKeys.Windows) != 0) mods |= 0x0008;   // MOD_WIN
        var vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);
        return (mods, vk);
    }

    private sealed record Registration(int WinId, KeyGesture Gesture, Action Callback)
    {
        public Registration WithGesture(KeyGesture newGesture) => this with { Gesture = newGesture };
    }
}