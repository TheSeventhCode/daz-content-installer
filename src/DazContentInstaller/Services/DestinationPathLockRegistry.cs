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
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static string CreateLockKey(Guid assetLibraryId, string installedRelativePath) =>
        $"{assetLibraryId:N}:{installedRelativePath}";
}
