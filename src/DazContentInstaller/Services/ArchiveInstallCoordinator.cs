using System;
using System.Threading;
using System.Threading.Tasks;

namespace DazContentInstaller.Services;

public sealed class ArchiveInstallCoordinator(SettingsService settingsService)
{
    private readonly object _sync = new();
    private SemaphoreSlim _capacity = new(1, 1);
    private int _configuredMax = 1;

    public int MaxConcurrentArchiveInstalls
    {
        get
        {
            EnsureCapacity();
            return _configuredMax;
        }
    }

    public bool AllowsConcurrentPersistence => MaxConcurrentArchiveInstalls > 1;

    public int ActiveArchiveInstallCount
    {
        get
        {
            lock (_sync)
                return _configuredMax - _capacity.CurrentCount;
        }
    }

    public async Task<IAsyncDisposable> AcquireArchiveInstallSlotAsync(CancellationToken cancellationToken = default)
    {
        var semaphore = EnsureCapacity();
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new InstallSlotLease(semaphore);
    }

    private SemaphoreSlim EnsureCapacity()
    {
        var desiredMax = Math.Max(1, settingsService.CurrentSettings.MaxConcurrentArchiveInstalls);
        lock (_sync)
        {
            if (_configuredMax == desiredMax)
                return _capacity;

            if (_capacity.CurrentCount == _configuredMax)
            {
                _capacity.Dispose();
                _capacity = new SemaphoreSlim(desiredMax, desiredMax);
                _configuredMax = desiredMax;
            }

            return _capacity;
        }
    }

    private sealed class InstallSlotLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();

            return ValueTask.CompletedTask;
        }
    }
}
