using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DazArchiveInstallerConcurrencyTests
{
    [Fact]
    public async Task InstallArchivesAsync_ReusesCachedScanResultDuringInstall()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new CountingArchiveScanner(new DazArchiveScanner(directoryService));
        var installer = fixture.CreateInstaller(scanner);

        var loadedArchive = new LoadedArchive(archivePath);
        var scan = await scanner.ScanArchiveAsync(archivePath);
        loadedArchive.ApplyScan(scan);
        scanner.ScanCount.ShouldBe(1);

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [loadedArchive]).ToListAsync();

        scanner.ScanCount.ShouldBe(1);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallArchivesAsync_InstallsMultipleArchivesConcurrentlyWithDisjointPaths()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync(config =>
            config.MaxConcurrentArchiveInstalls = 2);
        var firstArchive = fixture.CreateArchive("first.zip", ("data/first/file.txt", "first"));
        var secondArchive = fixture.CreateArchive("second.zip", ("data/second/file.txt", "second"));
        var installer = fixture.CreateInstaller();

        var queue = new[] { new LoadedArchive(firstArchive), new LoadedArchive(secondArchive) };
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, queue).ToListAsync();

        queue.All(x => x.ArchiveStatus == ArchiveStatus.Installed).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "first", "file.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "second", "file.txt")).ShouldBeTrue();
        (await fixture.DbContext.Archives.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task InstallArchivesAsync_PreservesReplacementSemanticsWhenArchivesConflictConcurrently()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync(config =>
            config.MaxConcurrentArchiveInstalls = 2);
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "first"));
        var secondArchive = fixture.CreateArchive("second.zip", ("data/shared/file.txt", "second version"));
        var installer = fixture.CreateInstaller();

        var queue = new[] { new LoadedArchive(firstArchive), new LoadedArchive(secondArchive) };
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, queue).ToListAsync();

        var records = (await fixture.DbContext.InstallRecords.ToListAsync()).OrderBy(x => x.InstalledAt).ToList();
        records.Count.ShouldBe(2);
        records.Count(x => x.HasBeenOverriden).ShouldBe(1);

        var finalContent = await File.ReadAllTextAsync(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt"));
        (finalContent == "first" || finalContent == "second version").ShouldBeTrue();
    }

    private sealed class CountingArchiveScanner(IDazArchiveScanner inner) : IDazArchiveScanner
    {
        public int ScanCount { get; private set; }

        public Task<DazArchiveScanResult> ScanArchiveAsync(string archivePath,
            CancellationToken cancellationToken = default)
        {
            ScanCount++;
            return inner.ScanArchiveAsync(archivePath, cancellationToken);
        }
    }
}