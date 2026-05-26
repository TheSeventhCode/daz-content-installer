using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveFileDetailsServiceTests
{
    [Fact]
    public async Task GetInstallRecordRowsAsync_ReturnsProjectedRowsForNestedArchiveTree()
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

        var service = new ArchiveFileDetailsService();
        var rows = await service.GetInstallRecordRowsAsync(fixture.DbContext, treeIds);

        rows.Count.ShouldBe(1);
        rows[0].ArchiveId.ShouldBe(archives.Single(x => x.ParentArchiveId is not null).Id);
        rows[0].InstalledRelativePath.ShouldContain("file.txt");
        rows[0].ArchiveRelativePath.ShouldContain("file.txt");
        rows[0].FileHash.ShouldNotBeNullOrWhiteSpace();
        rows[0].InstallRecordStatus.ShouldBe(Database.InstallRecordStatus.Installed);
        rows[0].HasBeenOverriden.ShouldBeFalse();
    }

    [Fact]
    public async Task GetInstallRecordRowsAsync_IncludesPendingRowsForFailedInstalls()
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

        var service = new ArchiveFileDetailsService();
        var rows = await service.GetInstallRecordRowsAsync(fixture.DbContext, [archive.Id]);

        rows.Count.ShouldBe(2);
        rows.Count(x => x.InstallRecordStatus == InstallRecordStatus.Pending).ShouldBe(1);
        rows.Count(x => x.InstallRecordStatus == InstallRecordStatus.Installed).ShouldBe(1);
    }

    [Fact]
    public async Task GetArchivesInTreeAsync_ReturnsArchiveMetadataForTreeMembers()
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
        var root = archives.Single(x => x.ParentArchiveId is null);
        var treeIds = ArchiveTreeResolver.GetTreeIds(
            archives.ToDictionary(x => x.Id, x => x.ParentArchiveId),
            root.Id);

        var service = new ArchiveFileDetailsService();
        var archiveInfos = await service.GetArchivesInTreeAsync(fixture.DbContext, treeIds);

        archiveInfos.Count.ShouldBe(treeIds.Count);
        archiveInfos.ShouldAllBe(x => treeIds.Contains(x.Id));
        archiveInfos.Single(x => x.Id == root.Id).ParentArchiveId.ShouldBeNull();
        archiveInfos.Count(x => x.ParentArchiveId == root.Id).ShouldBe(2);
    }

    [Fact]
    public async Task GetInstallRecordRowsAsync_ReturnsEmptyListForEmptyArchiveIds()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var service = new ArchiveFileDetailsService();

        var rows = await service.GetInstallRecordRowsAsync(fixture.DbContext, []);

        rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetArchivesInTreeAsync_ReturnsEmptyListForEmptyArchiveIds()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var service = new ArchiveFileDetailsService();

        var archiveInfos = await service.GetArchivesInTreeAsync(fixture.DbContext, []);

        archiveInfos.ShouldBeEmpty();
    }
}
