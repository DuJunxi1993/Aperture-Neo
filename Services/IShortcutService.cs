using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ApertureNeo.Services;

/// <summary>
/// Host-owned service that registers and dispatches OS-level
/// keyboard shortcuts (Win32 <c>RegisterHotKey</c>). Plugins and
/// features ask the service to bind a stable id ("Screenshot
/// Plugin.CaptureArea") to a <see cref="KeyGesture"/>; the
/// service hands back true if the registration succeeded and
/// invokes the callback when the user presses the binding from
/// anywhere on the desktop (the binding fires regardless of
/// which window has focus).
///
/// Stable id contract: plugin authors use <c>"&lt;PluginName&gt;.&lt;Action&gt;"</c>
/// (e.g. <c>"ScreenshotPlugin.CaptureArea"</c>). The settings
/// panel reads the same id namespace to render the per-action
/// shortcut row and to detect conflicts.
/// </summary>
public interface IShortcutService
{
    /// <summary>
    /// Register a global hotkey. The callback is invoked on the
    /// UI thread when the user presses the binding anywhere on
    /// the desktop. Returns <c>true</c> if Win32
    /// <c>RegisterHotKey</c> accepted the binding, <c>false</c>
    /// if it failed (e.g. another application already owns the
    /// same combination — silent failure, no exception).
    /// </summary>
    bool Register(string id, KeyGesture gesture, Action callback);

    /// <summary>
    /// Unregister a previously-registered id. Safe to call with
    /// unknown ids (no-op).
    /// </summary>
    void Unregister(string id);

    /// <summary>
    /// Atomically replace the gesture for an existing id. The
    /// old binding is unregistered, the new one is registered;
    /// if registration fails the id is left in an unregistered
    /// state. Returns <c>true</c> on success.
    /// </summary>
    bool Update(string id, KeyGesture gesture);

    /// <summary>
    /// Read the current gesture for an id. Returns false if the
    /// id isn't registered (e.g. registration failed earlier and
    /// the caller wants to surface the failure).
    /// </summary>
    bool TryGetGesture(string id, out KeyGesture gesture);

    /// <summary>
    /// Snapshot of all currently-registered id→gesture mappings.
    /// Updated as plugins activate / deactivate and as the
    /// settings panel mutates bindings.
    /// </summary>
    IReadOnlyDictionary<string, KeyGesture> Registered { get; }
}

/// <summary>
/// Optional internal extension point on <see cref="IShortcutService"/>
/// — exposes <c>IsActive</c> for the recorder to decide between
/// Update vs Register without re-implementing the service
/// surface. Casts to this interface are safe; default
/// implementation falls back to "always register" if not
/// implemented.
/// </summary>
public interface IShortcutService2 : IShortcutService
{
    /// <summary>True if the id is currently registered AND
    /// the OS accepted the binding.</summary>
    bool IsActive(string id);
}