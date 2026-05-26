using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DazContentInstaller.Tests;

public class InstallRecordStatisticsServiceTests
{
    [Fact]
    public async Task GetCountsForArchivesAsync_AggregatesNestedArchiveTree()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var innerArchivePath = fixture.CreateArchive("inner.zip", ("data/author/product/file.txt", "hello"));
        var outerArchivePath = fixture.CreateArchiveWithFiles("bundle.zip",
            [("IM0001-product.zip", innerArchivePath)]);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)]).ToListAsync();

        var archives = await fixture.DbContext.Archives.ToListAsync();
        var root = archives.Single(x => x.ParentArchiveId is null);
        var treeIds = ArchiveTreeResolver.GetTreeIds(
            archives.ToDictionary(x => x.Id, x => x.ParentArchiveId),
            root.Id);

        var statisticsService = new InstallRecordStatisticsService();
        var counts = await statisticsService.GetCountsForArchivesAsync(fixture.DbContext, treeIds);

        counts.FileCount.ShouldBe(1);
        counts.ActiveFileCount.ShouldBe(1);
        counts.ReplacedFileCount.ShouldBe(0);
        counts.SkippedFileCount.ShouldBe(0);
        counts.PendingFileCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetCountsForArchivesAsync_CountsPendingRowsSeparatelyFromActiveFiles()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip",
            ("data/author/product/file1.txt", "one"),
            ("data/author/product/file2.txt", "two"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.AssetFile)
            .OrderBy(x => x.AssetFile.ArchiveRelativePath)
            .ToListAsync();
        records[1].InstallRecordStatus = InstallRecordStatus.Pending;
        archive.Status = ArchiveStatus.Error;
        await fixture.DbContext.SaveChangesAsync();

        var statisticsService = new InstallRecordStatisticsService();
        var counts = await statisticsService.GetCountsForArchivesAsync(fixture.DbContext, [archive.Id]);

        counts.FileCount.ShouldBe(2);
        counts.ActiveFileCount.ShouldBe(1);
        counts.PendingFileCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetCountsGroupedByArchiveIdAsync_ReturnsSeparateCountsPerArchive()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var firstInner = fixture.CreateArchive("first-inner.zip", ("data/first/file.txt", "first"));
        var secondInner = fixture.CreateArchive("second-inner.zip", ("data/second/file.txt", "second"));
        var outerArchivePath = fixture.CreateCompositeArchive("bundle.zip", [],
            ("first-product.zip", firstInner),
            ("second-product.zip", secondInner));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)]).ToListAsync();

        var archives = await fixture.DbContext.Archives.ToListAsync();
        var archiveIds = archives.Select(x => x.Id).ToHashSet();

        var statisticsService = new InstallRecordStatisticsService();
        var countsByArchiveId =
            await statisticsService.GetCountsGroupedByArchiveIdAsync(fixture.DbContext, archiveIds);

        countsByArchiveId.Count.ShouldBe(2);
        countsByArchiveId.Values.ShouldAllBe(x => x.FileCount == 1);
        countsByArchiveId.Values.Sum(x => x.FileCount).ShouldBe(2);
        countsByArchiveId.Values.Sum(x => x.ActiveFileCount).ShouldBe(2);
    }

    [Fact]
    public async Task RollUpCounts_MatchesGroupedArchiveTotalsForNestedTree()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var firstInner = fixture.CreateArchive("first-inner.zip", ("data/first/file.txt", "first"));
        var secondInner = fixture.CreateArchive("second-inner.zip", ("data/second/file.txt", "second"));
        var outerArchivePath = fixture.CreateCompositeArchive("bundle.zip", [],
            ("first-product.zip", firstInner),
            ("second-product.zip", secondInner));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)]).ToListAsync();

        var archives = await fixture.DbContext.Archives.ToListAsync();
        var parentById = archives.ToDictionary(x => x.Id, x => x.ParentArchiveId);
        var root = archives.Single(x => x.ParentArchiveId is null);
        var treeIds = ArchiveTreeResolver.GetTreeIds(parentById, root.Id);

        var statisticsService = new InstallRecordStatisticsService();
        var countsByArchiveId =
            await statisticsService.GetCountsGroupedByArchiveIdAsync(fixture.DbContext, treeIds);
        var rolledUp = ArchiveTreeResolver.RollUpCounts(treeIds, countsByArchiveId);
        var directCounts = await statisticsService.GetCountsForArchivesAsync(fixture.DbContext, treeIds);

        rolledUp.ShouldBe(directCounts);
        rolledUp.FileCount.ShouldBe(2);
        rolledUp.ActiveFileCount.ShouldBe(2);
    }
}
