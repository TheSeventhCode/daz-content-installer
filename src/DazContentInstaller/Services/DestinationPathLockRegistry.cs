using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DazContentInstaller.Services;

public sealed class DestinationPathLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task ExecuteAsync(string lockKey, Func<Task> action, CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireAsync(lockKey, cancellationToken);
        await action();
    }

    public async Task<T> ExecuteAsync<T>(string lockKey, Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireAsync(lockKey, cancellationToken);
        return await action();
    }

    public async Task<IAsyncDisposable> AcquireAsync(string lockKey, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new LockLease(semaphore);
    }

    public static string CreateLockKey(Guid assetLibraryId, string installedRelativePath) =>
        $"{assetLibraryId:N}:{installedRelativePath}";

    private sealed class LockLease(SemaphoreSlim semaphore) : IAsyncDisposable
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
