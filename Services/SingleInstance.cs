using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ApertureNeo.Helpers;

namespace ApertureNeo.Services;

/// <summary>
/// Process-level single-instance enforcement. The first
/// <c>ApertureNeo.exe</c> process acquires a per-user named
/// <see cref="Mutex"/> and starts a <see cref="NamedPipeServerStream"/>
/// that listens for forwarded args from any subsequent launches.
/// Secondary launches detect the existing mutex, push their argv
/// through the pipe, and immediately <see cref="Application.Shutdown"/>
/// without creating a second tray icon, a second
/// <see cref="GlobalHotkeyService"/> registration, or a second
/// <see cref="MainWindow"/>. The primary instance receives the
/// forwarded args, marshals them to the UI thread, restores the
/// hidden main window, and re-runs the existing "open this image"
/// startup-file logic.
///
/// The standalone <c>ScreenshotTool.exe</c> uses a different
/// mutex/pipe name (<c>ApertureNeo-ScreenshotTool</c>) so it
/// can run independently of the main app.
///
/// Lifecycle:
/// <list type="number">
///   <item>Construct (does NOT touch Win32 — pure C# state).</item>
///   <item><see cref="EnsurePrimary"/> — at the very top of
///         <c>App.OnStartupCore</c>, BEFORE
///         <c>AppHost.Build</c> / <c>tray.Show</c> /
///         <c>ghk.Initialize</c>. Returns
///         <c>(isPrimary, instance)</c> where
///         <c>instance</c> is the gate object itself
///         (dispose it in <c>OnExit</c>).</item>
///   <item>If secondary, call <see cref="ForwardArgsAndExit"/>
///         to push argv to the primary and shut down.</item>
///   <item>On the primary, the gate's background pipe listener
///         fires the <see cref="ArgsReceived"/> event when a
///         secondary instance forwards args.</item>
/// </list>
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexBaseName = "ApertureNeo.SingleInstance";
    private const string PipeBaseName  = "ApertureNeo.Args";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _currentPipe;

    /// <summary>True when this process is the primary instance
    /// (i.e. it acquired the mutex).</summary>
    public bool IsPrimary { get; }

    private SingleInstance(Mutex mutex, bool ownsMutex, bool isPrimary, string pipeName)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        IsPrimary = isPrimary;
        _pipeName = pipeName;
        if (isPrimary)
            _ = RunPipeServerLoopAsync(_cts.Token);
    }

    /// <summary>Try to become the primary instance. Returns the
    /// gate object (always non-null). Caller should:
    /// <list type="bullet">
    ///   <item>If <see cref="IsPrimary"/> is true, hold the
    ///         returned <see cref="SingleInstance"/> for the
    ///         lifetime of the process and dispose it in
    ///         <c>App.OnExit</c>.</item>
    ///   <item>If false, immediately call
    ///         <see cref="ForwardArgsAndExit"/> with the current
    ///         argv, then <c>Application.Shutdown</c>.</item>
    /// </list>
    /// </summary>
    public static SingleInstance EnsurePrimary()
    {
        // Per-user so different Windows users on the same
        // machine don't fight over the singleton. The session
        // id is harder to access portably from .NET (WTSGetActiveConsoleSessionId
        // is the Win32 way; `Environment.CurrentSessionId`
        // doesn't exist in .NET). For now, the user name alone
        // is enough — two RDP sessions of the same user will
        // both try to become primary, and the second one's
        // Mutex init will succeed (Mutex is per-session, not
        // per-user), then the pipe will connect and the
        // second instance will forward and exit.
        var suffix = Environment.UserName;
        var mutexName = $"Local\\{MutexBaseName}.{suffix}";
        var pipeName  = $"{PipeBaseName}.{suffix}";

        bool createdNew;
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // Another process owns the mutex under this name
            // and we don't have access. Treat as "not primary."
            // (Rare — the user is running under a different
            // integrity level.)
            createdNew = false;
            mutex = new Mutex(false, mutexName);
        }

        var instance = new SingleInstance(mutex, ownsMutex: createdNew,
            isPrimary: createdNew, pipeName: pipeName);

        if (createdNew)
        {
            DebugLog.Write("SingleInstance",
                $"acquired primary mutex ({mutexName})");
        }
        else
        {
            DebugLog.Write("SingleInstance",
                $"another instance owns {mutexName}; this is secondary");
        }

        return instance;
    }

    /// <summary>Try to forward the current argv to the primary
    /// instance and exit. Called only by secondary instances.
    /// Returns true if forwarding succeeded, false if the
    /// primary didn't respond within the timeout (in which
    /// case the caller may want to fall back to running
    /// standalone, though for the main app we just bail).</summary>
    public bool ForwardArgsAndExit(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.Out, PipeOptions.None);
            // 2s timeout — primary is local so this is plenty,
            // but the user might have the app on a stalled
            // machine, in which case we give up rather than
            // hang the secondary.
            client.Connect(2000);

            var payload = string.Join("\0", args);
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            DebugLog.Write("SingleInstance",
                $"forwarded {args.Length} arg(s) to primary: {payload}");
            return true;
        }
        catch (TimeoutException)
        {
            DebugLog.Write("SingleInstance",
                "primary did not accept forwarded args within 2s; bailing");
            return false;
        }
        catch (IOException ex)
        {
            DebugLog.Write("SingleInstance",
                $"pipe IO failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Raised on the UI thread (Dispatcher) when
    /// forwarded args arrive from a secondary instance.
    /// Handlers should: restore the hidden MainWindow
    /// (tray → "显示主窗口" semantics), and if the args
    /// contain a supported file path, open it via the
    /// same logic as <c>RunViewerAsync</c>'s startup-file
    /// check.</summary>
    public event Action<string[]>? ArgsReceived;

    private async Task RunPipeServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _currentPipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await _currentPipe.WaitForConnectionAsync(ct).ConfigureAwait(false);

                // Read the full payload (one UTF-8 line,
                // NUL-separated args).
                using var ms = new MemoryStream();
                var buf = new byte[4096];
                int read;
                while ((read = await _currentPipe.ReadAsync(buf, 0, buf.Length, ct)
                                              .ConfigureAwait(false)) > 0)
                {
                    ms.Write(buf, 0, read);
                }
                var payload = Encoding.UTF8.GetString(ms.ToArray());
                var args = payload.Split('\0', StringSplitOptions.RemoveEmptyEntries);

                // Marshal to the UI thread so the handler can
                // touch WPF objects (MainWindow, Activation,
                // NavigationService, etc.) without an explicit
                // Dispatcher.Invoke.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        try { ArgsReceived?.Invoke(args); }
                        catch (Exception ex)
                        {
                            DebugLog.Write("SingleInstance",
                                $"ArgsReceived handler threw: {ex.GetType().Name}: {ex.Message}");
                        }
                    });
                }
                else
                {
                    try { ArgsReceived?.Invoke(args); }
                    catch (Exception ex)
                    {
                        DebugLog.Write("SingleInstance",
                            $"ArgsReceived handler threw (no dispatcher): {ex.GetType().Name}: {ex.Message}");
                    }
                }

                _currentPipe.Dispose();
                _currentPipe = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                DebugLog.Write("SingleInstance",
                    $"pipe loop iteration failed: {ex.GetType().Name}: {ex.Message}");
                _currentPipe?.Dispose();
                _currentPipe = null;
                // Brief pause before re-arming so a tight
                // failure loop doesn't peg the CPU.
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _currentPipe?.Dispose(); } catch { }
        try
        {
            if (_ownsMutex) _mutex.ReleaseMutex();
        }
        catch { /* mutex already abandoned or unowned */ }
        try { _mutex.Dispose(); } catch { }
        try { _cts.Dispose(); } catch { }
        DebugLog.Write("SingleInstance", "disposed");
    }
}
