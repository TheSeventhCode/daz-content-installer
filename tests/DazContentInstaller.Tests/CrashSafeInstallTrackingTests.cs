using System.IO.Compression;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DazContentInstaller.Tests;

public class CrashSafeInstallTrackingTests
{
    [Fact]
    public async Task InstallArchivesAsync_PrepopulatesManifestBeforeFirstCopy()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip",
            ("data/author/product/file1.txt", "one"),
            ("data/author/product/file2.txt", "two"));

        UnixFileMode? originalMode = null;
        try
        {
            originalMode = File.GetUnixFileMode(fixture.LibraryPath);
            File.SetUnixFileMode(fixture.LibraryPath,
                UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            var installer = fixture.CreateInstaller();
            var results = await installer
                .InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)])
                .ToListAsync();

            results.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Error);

            var records = await fixture.DbContext.InstallRecords.ToListAsync();
            records.Count.ShouldBe(2);
            records.ShouldAllBe(x => x.InstallRecordStatus == InstallRecordStatus.Pending);
            (await fixture.DbContext.AssetFiles.CountAsync()).ShouldBe(2);
        }
        finally
        {
            if (originalMode.HasValue)
                File.SetUnixFileMode(fixture.LibraryPath, originalMode.Value);
        }
    }

    [Fact]
    public async Task InstallArchivesAsync_LeavesDurableManifestRowsAfterMidInstallFailure()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var entries = Enumerable.Range(1, 3)
            .Select(i => ($"data/author/product/file{i}.txt", $"content-{i}"))
            .ToArray();
        var archivePath = fixture.CreateArchive("content.zip", entries);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.AssetFile)
            .OrderBy(x => x.AssetFile.ArchiveRelativePath)
            .ToListAsync();

        records.Count.ShouldBe(3);
        records[0].InstallRecordStatus = InstallRecordStatus.Pending;
        records[1].InstallRecordStatus = InstallRecordStatus.Pending;
        records[2].InstallRecordStatus = InstallRecordStatus.Pending;
        archive.Status = ArchiveStatus.Error;
        archive.ErrorMessage = "Simulated mid-install crash";
        await fixture.DbContext.SaveChangesAsync();

        fixture.DbContext.ChangeTracker.Clear();
        (await fixture.DbContext.InstallRecords.CountAsync()).ShouldBe(3);
        (await fixture.DbContext.InstallRecords.CountAsync(x => x.InstallRecordStatus == InstallRecordStatus.Pending))
            .ShouldBe(3);
        (await fixture.DbContext.Archives.SingleAsync()).Status.ShouldBe(ArchiveStatus.Error);
    }

    [Fact]
    public async Task UninstallArchiveAsync_RemovesCopiedFilesAndIgnoresPendingRowsOnErrorArchive()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip",
            ("data/author/product/file1.txt", "one"),
            ("data/author/product/file2.txt", "two"),
            ("data/author/product/file3.txt", "three"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.AssetFile)
            .OrderBy(x => x.AssetFile.ArchiveRelativePath)
            .ToListAsync();

        records[0].InstallRecordStatus.ShouldBe(InstallRecordStatus.Installed);
        records[1].InstallRecordStatus = InstallRecordStatus.Pending;
        records[2].InstallRecordStatus = InstallRecordStatus.Pending;
        archive.Status = ArchiveStatus.Error;
        archive.ErrorMessage = "Simulated crash";
        File.Delete(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file2.txt"));
        File.Delete(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file3.txt"));
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        await installer.UninstallArchiveAsync(archive.Id);

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file1.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file2.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file3.txt")).ShouldBeFalse();

        fixture.DbContext.ChangeTracker.Clear();
        var remaining = await fixture.DbContext.InstallRecords.ToListAsync();
        remaining.Count.ShouldBe(3);
        remaining.ShouldAllBe(x => x.InstallRecordStatus == InstallRecordStatus.Uninstalled);
        (await fixture.DbContext.Archives.SingleAsync()).Status.ShouldBe(ArchiveStatus.Uninstalled);
    }

    [Fact]
    public async Task InstallArchivesAsync_DoesNotTreatPendingRowsAsActiveOwnersDuringConcurrentInstall()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync(configureSettings: settings =>
            settings.MaxConcurrentArchiveInstalls = 2);
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "first"));
        var secondArchive = fixture.CreateArchive("second.zip", ("data/shared/file.txt", "second version"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id,
            [new LoadedArchive(firstArchive), new LoadedArchive(secondArchive)]).ToListAsync();

        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.Archive)
            .ToListAsync();
        records.Count.ShouldBe(2);
        records.Count(x => x.InstallRecordStatus == InstallRecordStatus.Pending).ShouldBe(0);
        records.Count(x => x.HasBeenOverriden).ShouldBe(1);
        records.Count(x => x.InstallRecordStatus == InstallRecordStatus.Installed).ShouldBe(1);
        records.Count(x => x.InstallRecordStatus == InstallRecordStatus.Replaced).ShouldBe(1);

        var finalContent = await File.ReadAllTextAsync(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt"));
        (finalContent == "first" || finalContent == "second version").ShouldBeTrue();
    }

    [Fact]
    public async Task RecoverInterruptedInstallsAsync_MarksLoadingAndInstallingArchivesAsError()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var loadingArchive = new Archive
        {
            ArchiveName = "loading.zip",
            ArchiveSize = 100,
            Status = ArchiveStatus.Loading,
            AssetLibraryId = fixture.AssetLibrary.Id,
            InstallStartedAt = DateTime.Now
        };
        var installingArchive = new Archive
        {
            ArchiveName = "installing.zip",
            ArchiveSize = 200,
            Status = ArchiveStatus.Installing,
            AssetLibraryId = fixture.AssetLibrary.Id,
            InstallStartedAt = DateTime.Now
        };
        fixture.DbContext.Archives.AddRange(loadingArchive, installingArchive);
        await fixture.DbContext.SaveChangesAsync();

        var recovery = new InterruptedInstallRecoveryService(fixture.DbContextFactory);
        await recovery.RecoverInterruptedInstallsAsync();

        fixture.DbContext.ChangeTracker.Clear();
        var archives = await fixture.DbContext.Archives.OrderBy(x => x.ArchiveName).ToListAsync();
        archives.Count.ShouldBe(2);
        archives.ShouldAllBe(x => x.Status == ArchiveStatus.Error);
        archives.ShouldAllBe(x => x.ErrorMessage == InterruptedInstallRecoveryService.InterruptedInstallMessage);
        archives.ShouldAllBe(x => x.InstallCompletedAt != null);
    }

    [Fact]
    public async Task InstallArchivesAsync_ResumesInterruptedArchiveInsteadOfDuplicate()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var interrupted = await fixture.DbContext.Archives.SingleAsync();
        interrupted.Status = ArchiveStatus.Installing;
        interrupted.ErrorMessage = InterruptedInstallRecoveryService.InterruptedInstallMessage;
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var retryResults = await installer
            .InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)])
            .ToListAsync();

        retryResults.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Installed);
        (await fixture.DbContext.Archives.CountAsync()).ShouldBe(1);
        (await fixture.DbContext.Archives.SingleAsync()).Status.ShouldBe(ArchiveStatus.Installed);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallArchivesAsync_DoesNotTreatFailedArchiveAsContentDuplicate()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var originalArchive = fixture.CreateArchive("original.zip", ("data/author/product/file.txt", "hello"));
        var renamedArchive = fixture.CreateArchive("renamed.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(originalArchive)])
            .ToListAsync();

        var failed = await fixture.DbContext.Archives.SingleAsync();
        failed.Status = ArchiveStatus.Error;
        failed.ErrorMessage = InterruptedInstallRecoveryService.InterruptedInstallMessage;
        failed.InstallCompletedAt = DateTime.Now;
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.ChangeTracker.Clear();

        var retryResults = await installer
            .InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(renamedArchive)])
            .ToListAsync();

        retryResults.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Installed);
        (await fixture.DbContext.Archives.CountAsync()).ShouldBe(2);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallArchivesAsync_PrepopulatesSharedInstalledFileRowsConcurrentlyWithoutUniqueConstraintFailure()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync(configureSettings: settings =>
            settings.MaxConcurrentArchiveInstalls = 2);
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "first"));
        var secondArchive = fixture.CreateArchive("second.zip", ("data/shared/file.txt", "second version"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id,
            [new LoadedArchive(firstArchive), new LoadedArchive(secondArchive)]).ToListAsync();

        (await fixture.DbContext.InstalledFiles.CountAsync()).ShouldBe(1);
        (await fixture.DbContext.InstallRecords.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task InstallArchivesAsync_PersistsInstalledRowsInFiveHundredFileBatches()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var blockerPath = Path.Combine(fixture.LibraryPath, "data", "batch", "blocker.txt");
        Directory.CreateDirectory(blockerPath);
        var entries = Enumerable.Range(1, 500)
            .Select(i => ($"data/batch/file{i:D3}.txt", $"content-{i}"))
            .Append(("data/batch/blocker.txt", "boom"))
            .ToArray();
        var archivePath = fixture.CreateArchive("content.zip", entries);
        var installer = fixture.CreateInstaller();

        var results = await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)])
            .ToListAsync();

        results.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Error);
        fixture.DbContext.ChangeTracker.Clear();

        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.AssetFile)
            .ToListAsync();
        records.Count.ShouldBe(501);
        records.Count(x => x.InstallRecordStatus == InstallRecordStatus.Installed).ShouldBe(500);
        records.Count(x => x.InstallRecordStatus == InstallRecordStatus.Pending).ShouldBe(1);
        records.Single(x => x.InstallRecordStatus == InstallRecordStatus.Pending)
            .AssetFile.InstalledRelativePath.ShouldBe(Path.Combine("data", "batch", "blocker.txt"));
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "batch", "file500.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task UninstallArchiveAsync_RestoresInterruptedReplacementBackup()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "old"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(firstArchive)]).ToListAsync();

        var blockerPath = Path.Combine(fixture.LibraryPath, "data", "shared", "blocker.txt");
        Directory.CreateDirectory(blockerPath);
        var secondArchive = fixture.CreateArchive("second.zip",
            ("data/shared/file.txt", "new"),
            ("data/shared/blocker.txt", "boom"));

        var results = await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(secondArchive)])
            .ToListAsync();

        results.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Error);
        (await File.ReadAllTextAsync(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")))
            .ShouldBe("new");

        fixture.DbContext.ChangeTracker.Clear();
        var interruptedArchive = await fixture.DbContext.Archives.SingleAsync(x => x.ArchiveName == "second.zip");
        var operation = await fixture.DbContext.InstallFileOperations.SingleAsync();
        operation.Status.ShouldBe(InstallFileOperationStatus.Pending);
        operation.BackupFilePath.ShouldNotBeNull();
        File.Exists(operation.BackupFilePath).ShouldBeTrue();

        await installer.UninstallArchiveAsync(interruptedArchive.Id);

        (await File.ReadAllTextAsync(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")))
            .ShouldBe("old");
        File.Exists(operation.BackupFilePath).ShouldBeFalse();

        fixture.DbContext.ChangeTracker.Clear();
        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.Archive)
            .ToListAsync();
        var originalRecord = records.Single(x => x.Archive.ArchiveName == "first.zip");
        originalRecord.InstallRecordStatus.ShouldBe(InstallRecordStatus.Installed);
        originalRecord.HasBeenOverriden.ShouldBeFalse();

        records.Where(x => x.Archive.ArchiveName == "second.zip")
            .ShouldAllBe(x => x.InstallRecordStatus == InstallRecordStatus.Uninstalled);
        (await fixture.DbContext.InstallFileOperations.SingleAsync()).Status.ShouldBe(InstallFileOperationStatus.Restored);
    }
}
