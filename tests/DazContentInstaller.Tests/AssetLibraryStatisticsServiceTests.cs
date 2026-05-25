using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class AssetLibraryStatisticsServiceTests
{
    [Fact]
    public async Task GetStatisticsAsync_AggregatesLibraryTotalsFromDatabase()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var statisticsService = new AssetLibraryStatisticsService(fixture.DbContext);
        var statistics = await statisticsService.GetStatisticsAsync(fixture.AssetLibrary.Id);

        statistics.ArchiveCount.ShouldBe(1);
        statistics.InstalledArchiveCount.ShouldBe(1);
        statistics.FileCount.ShouldBe(1);
        statistics.ActiveFileCount.ShouldBe(1);
        statistics.TotalArchiveSize.ShouldBeGreaterThan(0UL);
        statistics.TotalContentSize.ShouldBeGreaterThan(0UL);
    }
}
