using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task InstallArchivesAsync_EnforcesGlobalLimitAcrossConcurrentJobs()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync(configureSettings: settings =>
            settings.MaxConcurrentArchiveInstalls = 2);

        var coordinator = new ArchiveInstallCoordinator(fixture.SettingsService);
        var installerA = fixture.CreateInstaller(archiveInstallCoordinator: coordinator);
        var installerB = fixture.CreateInstaller(archiveInstallCoordinator: coordinator);

        var batchA = new[]
        {
            new LoadedArchive(fixture.CreateArchive("job-a-1.zip", ("data/a1/file.txt", "a1"))),
            new LoadedArchive(fixture.CreateArchive("job-a-2.zip", ("data/a2/file.txt", "a2")))
        };
        var batchB = new[]
        {
            new LoadedArchive(fixture.CreateArchive("job-b-1.zip", ("data/b1/file.txt", "b1"))),
            new LoadedArchive(fixture.CreateArchive("job-b-2.zip", ("data/b2/file.txt", "b2")))
        };

        var peakActive = 0;
        using var peakMonitorCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var peakMonitor = Task.Run(async () =>
        {
            while (!peakMonitorCts.Token.IsCancellationRequested)
            {
                peakActive = Math.Max(peakActive, coordinator.ActiveArchiveInstallCount);
                await Task.Delay(10, peakMonitorCts.Token);
            }
        }, peakMonitorCts.Token);

        var installTaskA = installerA.InstallArchivesAsync(fixture.AssetLibrary.Id, batchA).ToListAsync();
        var installTaskB = installerB.InstallArchivesAsync(fixture.AssetLibrary.Id, batchB).ToListAsync();

        await Task.WhenAll(installTaskA.AsTask(), installTaskB.AsTask());
        peakMonitorCts.Cancel();

        try
        {
            await peakMonitor;
        }
        catch (OperationCanceledException)
        {
        }

        peakActive.ShouldBeLessThanOrEqualTo(2);
        peakActive.ShouldBeGreaterThanOrEqualTo(2);
        batchA.All(x => x.ArchiveStatus == ArchiveStatus.Installed).ShouldBeTrue();
        batchB.All(x => x.ArchiveStatus == ArchiveStatus.Installed).ShouldBeTrue();
        (await fixture.DbContext.Archives.CountAsync()).ShouldBe(4);
    }
}
