using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DestinationPathLockRegistryTests
{
    [Fact]
    public void CreateLockKey_FormatsAssetLibraryAndPath()
    {
        var libraryId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        DestinationPathLockRegistry.CreateLockKey(libraryId, "data/shared/file.txt")
            .ShouldBe("11111111222233334444555555555555:data/shared/file.txt");
    }

    [Fact]
    public async Task ExecuteAsync_SerializesConcurrentAccessForSameKey()
    {
        var registry = new DestinationPathLockRegistry();
        const string lockKey = "shared-key";
        var activeCount = 0;
        var maxActiveCount = 0;

        await Task.WhenAll(RunAsync(), RunAsync());

        maxActiveCount.ShouldBe(1);
        return;

        async Task RunAsync()
        {
            await registry.ExecuteAsync(lockKey, async () =>
            {
                activeCount++;
                maxActiveCount = Math.Max(maxActiveCount, activeCount);
                await Task.Delay(50);
                activeCount--;
            });
        }
    }

    [Fact]
    public async Task ExecuteAsync_AllowsParallelAccessForDifferentKeys()
    {
        var registry = new DestinationPathLockRegistry();
        var activeCount = 0;
        var maxActiveCount = 0;

        await Task.WhenAll(RunAsync("first"), RunAsync("second"));

        maxActiveCount.ShouldBe(2);
        return;

        async Task RunAsync(string lockKey)
        {
            await registry.ExecuteAsync(lockKey, async () =>
            {
                activeCount++;
                maxActiveCount = Math.Max(maxActiveCount, activeCount);
                await Task.Delay(50);
                activeCount--;
            });
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsResultFromAction()
    {
        var registry = new DestinationPathLockRegistry();

        var result = await registry.ExecuteAsync("key", () => Task.FromResult(42));

        result.ShouldBe(42);
    }
}