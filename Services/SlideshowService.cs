using System;
using System.Timers;
using Timer = System.Timers.Timer;

namespace ApertureNeo.Services;

/// <summary>
/// Timer-driven auto-advance for the viewer. Fires
/// <see cref="NextRequested"/> every <see cref="IntervalMs"/>
/// milliseconds while running; the host window subscribes to
/// that event and calls <c>NavigationService.MoveNext</c> on
/// the UI thread.
/// </summary>
public class SlideshowService : IDisposable
{
    private readonly Timer _timer;
    private bool _isRunning;
    private int _intervalMs = 3000;

    public event Action? NextRequested;
    public event Action? Stopped;

    public bool IsRunning => _isRunning;
    public int IntervalMs
    {
        get => _intervalMs;
        set
        {
            _intervalMs = Math.Clamp(value, 500, 60000);
            if (_isRunning)
            {
                _timer.Interval = _intervalMs;
            }
        }
    }

    public SlideshowService()
    {
        _timer = new Timer(_intervalMs);
        _timer.AutoReset = true;
        _timer.Elapsed += (_, _) => NextRequested?.Invoke();
    }

    /// <summary>Start the slideshow timer. No-op if already
    /// running. The first <see cref="NextRequested"/> fires after
    /// <see cref="IntervalMs"/> has elapsed, not immediately.</summary>
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _timer.Interval = _intervalMs;
        _timer.Start();
    }

    /// <summary>Stop the slideshow timer. Fires <see cref="Stopped"/>
    /// so the host can refresh the play/pause button icon.</summary>
    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer.Stop();
        Stopped?.Invoke();
    }

    /// <summary>Toggle between running and stopped. Equivalent to
    /// <see cref="Stop"/> when running, <see cref="Start"/> otherwise.</summary>
    public void Toggle()
    {
        if (_isRunning) Stop();
        else Start();
    }

    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}