using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveIdentityLockRegistryTests
{
    [Fact]
    public async Task ExecuteAsync_SerializesConcurrentAccessForSameKey()
    {
        var registry = new ArchiveIdentityLockRegistry();
        const string lockKey = "shared-archive";
        var activeCount = 0;
        var maxActiveCount = 0;

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await registry.ExecuteAsync(lockKey, async () =>
            {
                var current = Interlocked.Increment(ref activeCount);
                maxActiveCount = Math.Max(maxActiveCount, current);
                await Task.Delay(25);
                Interlocked.Decrement(ref activeCount);
                return 0;
            });
        });

        await Task.WhenAll(tasks);
        maxActiveCount.ShouldBe(1);
    }
}
