using System;

namespace DazContentInstaller;

internal sealed class ProgressUpdateThrottler(TimeSpan minimumInterval)
{
    private long _lastUpdateTicks;

    public bool ShouldUpdate(bool force = false)
    {
        if (force)
        {
            _lastUpdateTicks = Environment.TickCount64;
            return true;
        }

        var now = Environment.TickCount64;
        if (now - _lastUpdateTicks < minimumInterval.TotalMilliseconds)
            return false;

        _lastUpdateTicks = now;
        return true;
    }
}
