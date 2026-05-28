using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveInstallCoordinatorTests
{
    [Fact]
    public async Task AcquireArchiveInstallSlotAsync_RespectsConfiguredMaximum()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync(configureSettings: settings =>
            settings.MaxConcurrentArchiveInstalls = 2);
        var coordinator = new ArchiveInstallCoordinator(fixture.SettingsService);

        var first = await coordinator.AcquireArchiveInstallSlotAsync();
        var second = await coordinator.AcquireArchiveInstallSlotAsync();

        coordinator.ActiveArchiveInstallCount.ShouldBe(2);

        var thirdAcquire = coordinator.AcquireArchiveInstallSlotAsync();
        thirdAcquire.IsCompleted.ShouldBeFalse();

        await first.DisposeAsync();
        await second.DisposeAsync();
        await using (await thirdAcquire)
        {
            coordinator.ActiveArchiveInstallCount.ShouldBe(1);
        }

        coordinator.ActiveArchiveInstallCount.ShouldBe(0);
    }

}
