using System;
using Avalonia.Threading;

namespace HostsManager.Services;

public sealed class DispatcherTimerAdapter : IUiTimer
{
    private readonly DispatcherTimer timer;

    public DispatcherTimerAdapter(TimeSpan interval)
    {
        timer = new DispatcherTimer
        {
            Interval = interval
        };
        timer.Tick += (_, e) => Tick?.Invoke(this, e);
    }

    public event EventHandler? Tick;

    public TimeSpan Interval
    {
        get => timer.Interval;
        set => timer.Interval = value;
    }

    public void Start()
    {
        timer.Start();
    }

    public void Stop()
    {
        timer.Stop();
    }
}
