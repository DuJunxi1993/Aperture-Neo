using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ApertureNeo.Services;

/// <summary>
/// A dedicated STA thread with a WPF Dispatcher for WIC
/// <c>BitmapDecoder</c> / <c>BitmapImage</c> operations that
/// require STA apartment state. Tasks are queued and executed
/// sequentially on this background thread so the UI thread is
/// never blocked by decode work. The thread is shut down when
/// this instance is disposed.
/// </summary>
public sealed class SingleStaThread : IDisposable
{
    private Dispatcher _dispatcher = null!;
    private readonly Thread _thread;
    private bool _disposed;

    public SingleStaThread()
    {
        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            Name = "WicDecode",
            IsBackground = true
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    public Task<T> RunAsync<T>(Func<T> func, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task;
    }

    public Task RunAsync(Action action, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dispatcher.InvokeShutdown();
    }
}
