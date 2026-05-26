using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DazContentInstaller.Services;

public sealed class ArchiveIdentityLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<T> ExecuteAsync<T>(string lockKey, Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static string CreateLockKey(Guid assetLibraryId, string archiveName, ulong archiveSize) =>
        $"{assetLibraryId:N}:{archiveName}:{archiveSize}";
}
