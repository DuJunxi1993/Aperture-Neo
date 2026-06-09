using System;
using System.Timers;
using Timer = System.Timers.Timer;

namespace ApertureNeo.Services;

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

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _timer.Interval = _intervalMs;
        _timer.Start();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _timer.Stop();
        Stopped?.Invoke();
    }

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