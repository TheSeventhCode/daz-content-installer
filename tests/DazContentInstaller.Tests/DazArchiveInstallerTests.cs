using System.IO.Compression;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DazArchiveInstallerTests
{
    [Fact]
    public async Task InstallArchivesAsync_InstallsFilesAndBacksUpOriginalArchive()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        var results = await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)])
            .ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.BackupPath, "content.zip")).ShouldBeTrue();
        results.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Installed);

        var archive = await fixture.DbContext.Archives.Include(x => x.AssetFiles).SingleAsync();
        archive.Status.ShouldBe(ArchiveStatus.Installed);
        archive.ArchiveName.ShouldBe("content.zip");
        archive.DisplayName.ShouldBeNull();
        archive.AssetFiles.Single().InstalledRelativePath
            .ShouldBe(Path.Combine("data", "author", "product", "file.txt"));
    }

    [Fact]
    public async Task InstallArchivesAsync_PersistsDisplayNameFromSupplementDsx()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        const string supplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Business Presentation Poses"/>
            </ProductSupplement>
            """;
        var archivePath = fixture.CreateArchive("content.zip",
            ("Supplement.dsx", supplement),
            ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        archive.ArchiveName.ShouldBe("content.zip");
        archive.DisplayName.ShouldBe("Business Presentation Poses");
        File.Exists(Path.Combine(fixture.BackupPath, "content.zip")).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallArchivesAsync_PersistsNestedDisplayNameFromSupplementDsx()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        const string supplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Business Presentation Poses"/>
            </ProductSupplement>
            """;
        var innerArchivePath = fixture.CreateArchive("inner.zip",
            ("Supplement.dsx", supplement),
            ("data/author/product/file.txt", "hello"));
        var outerArchivePath = fixture.CreateArchiveWithFiles("bundle.zip",
            ("IM0001-product.zip", innerArchivePath));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)])
            .ToListAsync();

        var archives = await fixture.DbContext.Archives
            .OrderBy(x => x.ParentArchiveId == null ? 0 : 1)
            .ToListAsync();
        archives.Count.ShouldBe(2);

        var root = archives.Single(x => x.ParentArchiveId is null);
        var child = archives.Single(x => x.ParentArchiveId == root.Id);

        root.ArchiveName.ShouldBe("bundle.zip");
        root.DisplayName.ShouldBe("Business Presentation Poses");
        child.ArchiveName.ShouldBe("IM0001-product.zip");
        child.DisplayName.ShouldBe("Business Presentation Poses");
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallArchivesAsync_CopiesTopLevelThumbnailUsingArchiveId()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip",
            ("cover.png", "thumbnail"),
            ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        archive.ThumbnailFileExtension.ShouldBe(".png");
        File.Exists(Path.Combine(fixture.ThumbnailPath, $"{archive.Id}.png")).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallArchivesAsync_OnlyStoresThumbnailForTopLevelArchive()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var innerArchivePath = fixture.CreateArchive("inner.zip",
            ("cover.png", "nested-thumbnail"),
            ("data/author/product/file.txt", "hello"));
        var outerArchivePath = fixture.CreateCompositeArchive("bundle.zip",
            [("cover.jpg", "root-thumbnail")],
            ("IM0001-product.zip", innerArchivePath));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)])
            .ToListAsync();

        var archives = await fixture.DbContext.Archives
            .OrderBy(x => x.ParentArchiveId == null ? 0 : 1)
            .ToListAsync();
        var root = archives.Single(x => x.ParentArchiveId is null);
        var child = archives.Single(x => x.ParentArchiveId == root.Id);

        root.ThumbnailFileExtension.ShouldBe(".jpg");
        child.ThumbnailFileExtension.ShouldBeNull();
        File.Exists(Path.Combine(fixture.ThumbnailPath, $"{root.Id}.jpg")).ShouldBeTrue();
        Directory.EnumerateFiles(fixture.ThumbnailPath).ShouldBe([Path.Combine(fixture.ThumbnailPath, $"{root.Id}.jpg")]);
    }

    [Fact]
    public async Task InstallArchivesAsync_PersistsMultipleNestedDisplayNames()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        const string firstSupplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="First Product"/>
            </ProductSupplement>
            """;
        const string secondSupplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Second Product"/>
            </ProductSupplement>
            """;
        var firstInner = fixture.CreateArchive("first-inner.zip",
            ("Supplement.dsx", firstSupplement),
            ("data/first/file.txt", "first"));
        var secondInner = fixture.CreateArchive("second-inner.zip",
            ("Supplement.dsx", secondSupplement),
            ("data/second/file.txt", "second"));
        var outerArchivePath = fixture.CreateCompositeArchive("bundle.zip", [],
            ("first-product.zip", firstInner),
            ("second-product.zip", secondInner));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)])
            .ToListAsync();

        var childArchives = await fixture.DbContext.Archives
            .Where(x => x.ParentArchiveId != null)
            .OrderBy(x => x.ArchiveName)
            .ToListAsync();

        childArchives.Count.ShouldBe(2);
        childArchives[0].DisplayName.ShouldBe("First Product");
        childArchives[1].DisplayName.ShouldBe("Second Product");
    }

    [Fact]
    public async Task ReinstallArchiveAsync_UpdatesNestedDisplayNameFromSupplementDsx()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        const string supplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Business Presentation Poses"/>
            </ProductSupplement>
            """;
        var innerArchivePath = fixture.CreateArchive("inner.zip",
            ("Supplement.dsx", supplement),
            ("data/author/product/file.txt", "hello"));
        var outerArchivePath = fixture.CreateArchiveWithFiles("bundle.zip",
            ("IM0001-product.zip", innerArchivePath));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)]).ToListAsync();

        var root = await fixture.DbContext.Archives.SingleAsync(x => x.ParentArchiveId == null);
        var child = await fixture.DbContext.Archives.SingleAsync(x => x.ParentArchiveId == root.Id);
        child.DisplayName = null;
        await fixture.DbContext.SaveChangesAsync();

        await installer.UninstallArchiveAsync(root.Id);
        fixture.DbContext.ChangeTracker.Clear();

        var queueItem = new LoadedArchive(Path.Combine(fixture.BackupPath, "bundle.zip"))
        {
            ArchiveId = root.Id
        };

        await installer.ReinstallArchiveAsync(root.Id, queueItem).ToListAsync();

        fixture.DbContext.ChangeTracker.Clear();
        child = await fixture.DbContext.Archives.SingleAsync(x => x.ParentArchiveId == root.Id);
        child.DisplayName.ShouldBe("Business Presentation Poses");
    }

    [Fact]
    public async Task ReinstallArchiveAsync_RefreshesThumbnailAndRemovesOldExtension()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var originalArchivePath = fixture.CreateArchive("content.zip",
            ("cover.png", "original-thumbnail"),
            ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(originalArchivePath)])
            .ToListAsync();

        var root = await fixture.DbContext.Archives.SingleAsync(x => x.ParentArchiveId == null);
        var originalThumbnailPath = Path.Combine(fixture.ThumbnailPath, $"{root.Id}.png");
        File.Exists(originalThumbnailPath).ShouldBeTrue();

        await installer.UninstallArchiveAsync(root.Id);
        fixture.DbContext.ChangeTracker.Clear();

        var replacementArchivePath = fixture.CreateArchive("replacement.zip",
            ("cover.jpg", "replacement-thumbnail"),
            ("data/author/product/file.txt", "hello"));
        File.Copy(replacementArchivePath, Path.Combine(fixture.BackupPath, "content.zip"), overwrite: true);

        var queueItem = new LoadedArchive(Path.Combine(fixture.BackupPath, "content.zip"))
        {
            ArchiveId = root.Id
        };

        await installer.ReinstallArchiveAsync(root.Id, queueItem).ToListAsync();

        fixture.DbContext.ChangeTracker.Clear();
        root = await fixture.DbContext.Archives.SingleAsync(x => x.ParentArchiveId == null);
        root.ThumbnailFileExtension.ShouldBe(".jpg");
        File.Exists(originalThumbnailPath).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.ThumbnailPath, $"{root.Id}.jpg")).ShouldBeTrue();
    }

    [Fact]
    public void LoadedArchive_ApplyScan_PromotesSingleSubArchiveDisplayName()
    {
        var scan = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            NestedArchives =
            [
                new DazArchiveScanResult
                {
                    ArchiveName = "product.zip",
                    ArchivePath = "product.zip",
                    DisplayName = "Business Poses",
                    InstallableFiles =
                    [
                        new DazArchiveScanEntry
                        {
                            ArchiveRelativePath = "data/a/file.txt",
                            InstalledRelativePath = "data/a/file.txt",
                            FileSize = 10
                        }
                    ]
                }
            ]
        };

        var loaded = new LoadedArchive("/tmp/bundle.zip");
        loaded.ApplyScan(scan);

        loaded.DisplayName.ShouldBe("Business Poses");
        loaded.HasSubArchives.ShouldBeFalse();
    }

    [Fact]
    public async Task InstallArchivesAsync_DeduplicatesAssetTypesAfterQueueScanAndInstall()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        var firstInner = fixture.CreateArchive("first-inner.zip",
            ("environments/author/product/scene.duf", "scene"),
            ("data/author/materials/product/surface.duf", "surface"),
            ("runtime/textures/author/product/texture.jpg", "texture"));
        var secondInner = fixture.CreateArchive("second-inner.zip",
            ("environments/author/product/scene2.duf", "scene"),
            ("data/author/materials/product/surface2.duf", "surface"),
            ("runtime/textures/author/product/texture2.jpg", "texture"));
        var outerArchivePath = fixture.CreateCompositeArchive("bundle.zip", [],
            ("IM00054761-01_WinterblackSanctuaryFallenDS.zip", firstInner),
            ("IM00054761-02_WinterblackSanctuaryFallenPs.zip", secondInner));
        var scan = await scanner.ScanArchiveAsync(outerArchivePath);
        var loaded = new LoadedArchive(outerArchivePath);
        loaded.ApplyScan(scan);
        loaded.AssetTypes.Count.ShouldBe(3);

        var installer = fixture.CreateInstaller();
        LoadedArchive? latest = null;
        await foreach (var progress in installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [loaded]))
            latest = progress;

        latest.ShouldNotBeNull();
        latest.AssetTypes.Count.ShouldBe(3);
        latest.AssetTypes.ShouldBe([AssetType.Environment, AssetType.Materials, AssetType.Textures],
            ignoreOrder: true);
    }

    [Fact]
    public async Task InstallArchivesAsync_InstallsContentFromNestedArchives()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var nestedArchivePath = fixture.CreateArchive("inner.zip", ("Runtime/Textures/thing.jpg", "image"));
        var outerArchivePath =
            fixture.CreateArchiveWithFiles("bundle.zip", [("IM0001-product.zip", nestedArchivePath)]);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)])
            .ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "Runtime", "Textures", "thing.jpg")).ShouldBeTrue();

        var archives = await fixture.DbContext.Archives.ToListAsync();
        archives.Count.ShouldBe(2);
        archives.Count(x => x.ParentArchiveId is not null).ShouldBe(1);
    }

    [Fact]
    public async Task InstallArchivesAsync_CanonicalizesRuntimePathCasing()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("runtime.zip", ("runtime/textures/thing.jpg", "image"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "Runtime", "Textures", "thing.jpg")).ShouldBeTrue();
        Directory.Exists(Path.Combine(fixture.LibraryPath, "runtime")).ShouldBeFalse();

        var archive = await fixture.DbContext.Archives.Include(x => x.AssetFiles).SingleAsync();
        archive.AssetFiles.Single().InstalledRelativePath
            .ShouldBe(Path.Combine("Runtime", "Textures", "thing.jpg"));
    }

    [Fact]
    public async Task InstallArchivesAsync_RecordsReplacementsWithInstallRecords()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "first"));
        var secondArchive = fixture.CreateArchive("second.zip", ("data/shared/file.txt", "second version"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(firstArchive)]).ToListAsync();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(secondArchive)]).ToListAsync();

        var records = (await fixture.DbContext.InstallRecords.ToListAsync()).OrderBy(x => x.InstalledAt).ToList();
        records.Count.ShouldBe(2);
        records[0].HasBeenOverriden.ShouldBeTrue();
        records[0].InstallRecordStatus.ShouldBe(InstallRecordStatus.Installed);
        records[1].HasBeenOverriden.ShouldBeFalse();
        records[1].InstallRecordStatus.ShouldBe(InstallRecordStatus.Replaced);
        (await File.ReadAllTextAsync(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")))
            .ShouldBe("second version");
    }

    [Fact]
    public async Task InstallArchivesAsync_BatchesInstallRecordsWhenSingleThreaded()
    {
        const int fileCount = 25;
        var entries = Enumerable.Range(1, fileCount)
            .Select(i => ($"data/author/product/file{i:D3}.txt", $"content-{i}"))
            .ToArray();

        await using var fixture = await InstallerFixture.CreateAsync(configureSettings: settings =>
            settings.MaxConcurrentArchiveInstalls = 1);
        var archivePath = fixture.CreateArchive("many-files.zip", entries);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        (await fixture.DbContext.InstallRecords.CountAsync()).ShouldBe(fileCount);
        (await fixture.DbContext.AssetFiles.CountAsync()).ShouldBe(fileCount);
        (await fixture.DbContext.InstalledFiles.CountAsync()).ShouldBe(fileCount);

        foreach (var (entryPath, content) in entries)
        {
            var relativePath = entryPath.Replace('/', Path.DirectorySeparatorChar);
            var installedPath = Path.Combine(fixture.LibraryPath, relativePath);
            File.Exists(installedPath).ShouldBeTrue();
            (await File.ReadAllTextAsync(installedPath)).ShouldBe(content);
        }
    }

    [Fact]
    public async Task InstallArchivesAsync_ReplacesCaseVariantPathsWithSameLogicalDestination()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "first"));
        var secondArchive = fixture.CreateArchive("second.zip", ("DATA/shared/file.txt", "second version"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(firstArchive)]).ToListAsync();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(secondArchive)]).ToListAsync();

        (await fixture.DbContext.InstalledFiles.CountAsync()).ShouldBe(1);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(fixture.LibraryPath, "DATA")).ShouldBeFalse();

        var records = (await fixture.DbContext.InstallRecords.ToListAsync()).OrderBy(x => x.InstalledAt).ToList();
        records.Count.ShouldBe(2);
        records[1].InstallRecordStatus.ShouldBe(InstallRecordStatus.Replaced);
        (await File.ReadAllTextAsync(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")))
            .ShouldBe("second version");
    }

    [Fact]
    public async Task InstallArchivesAsync_SkipsCaseVariantPathsWithSameContent()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "same content"));
        var secondArchive = fixture.CreateArchive("second.zip",
            ("DATA/shared/file.txt", "same content"),
            ("data/unique/extra.txt", "extra"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(firstArchive)]).ToListAsync();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(secondArchive)]).ToListAsync();

        (await fixture.DbContext.InstalledFiles.CountAsync()).ShouldBe(2);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "unique", "extra.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(fixture.LibraryPath, "DATA")).ShouldBeFalse();

        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.AssetFile)
            .Include(x => x.Archive)
            .ToListAsync();
        records.Count(x =>
                x.Archive.ArchiveName == "second.zip" &&
                x.AssetFile.InstalledRelativePath == Path.Combine("data", "shared", "file.txt") &&
                x.InstallRecordStatus == InstallRecordStatus.Skipped)
            .ShouldBe(1);
        records.Count(x =>
                x.Archive.ArchiveName == "second.zip" &&
                x.AssetFile.InstalledRelativePath == Path.Combine("data", "unique", "extra.txt") &&
                x.InstallRecordStatus == InstallRecordStatus.Installed)
            .ShouldBe(1);
    }

    [Fact]
    public async Task InstallArchivesAsync_UsesExistingLibraryPathCasing()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        Directory.CreateDirectory(Path.Combine(fixture.LibraryPath, "Data", "Author"));
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "Data", "Author", "product", "file.txt")).ShouldBeTrue();
        Directory.Exists(Path.Combine(fixture.LibraryPath, "data")).ShouldBeFalse();

        var archive = await fixture.DbContext.Archives.Include(x => x.AssetFiles).SingleAsync();
        archive.AssetFiles.Single().InstalledRelativePath
            .ShouldBe(Path.Combine("Data", "Author", "product", "file.txt"));
    }

    [Fact]
    public async Task InstallArchivesAsync_IgnoresThumbsDbFiles()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip",
            ("data/author/product/file.txt", "hello"),
            ("data/author/product/Thumbs.db", "thumbnail cache"),
            ("Runtime/Textures/Thumbs.db", "thumbnail cache"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "Thumbs.db")).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.LibraryPath, "Runtime", "Thumbs.db")).ShouldBeFalse();

        var archive = await fixture.DbContext.Archives.Include(x => x.AssetFiles).SingleAsync();
        archive.AssetFiles.Count.ShouldBe(1);
        archive.AssetFiles.Single().InstalledRelativePath
            .ShouldBe(Path.Combine("data", "author", "product", "file.txt"));
    }

    [Fact]
    public async Task ReinstallArchiveAsync_UpdatesDisplayNameFromSupplementDsx()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        const string supplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Business Presentation Poses"/>
            </ProductSupplement>
            """;
        var archivePath = fixture.CreateArchive("content.zip",
            ("Supplement.dsx", supplement),
            ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archiveEntity = await fixture.DbContext.Archives.SingleAsync();
        archiveEntity.DisplayName = null;
        await fixture.DbContext.SaveChangesAsync();

        await installer.UninstallArchiveAsync(archiveEntity.Id);
        fixture.DbContext.ChangeTracker.Clear();

        var queueItem = new LoadedArchive(Path.Combine(fixture.BackupPath, "content.zip"))
        {
            ArchiveId = archiveEntity.Id
        };

        await installer.ReinstallArchiveAsync(archiveEntity.Id, queueItem).ToListAsync();

        fixture.DbContext.ChangeTracker.Clear();
        archiveEntity = await fixture.DbContext.Archives.SingleAsync();
        archiveEntity.DisplayName.ShouldBe("Business Presentation Poses");
        queueItem.DisplayName.ShouldBe("Business Presentation Poses");
    }

    [Fact]
    public async Task ReinstallArchiveAsync_ReinstallsFromBackupAfterUninstall()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archiveEntity = await fixture.DbContext.Archives.SingleAsync();
        await installer.UninstallArchiveAsync(archiveEntity.Id);

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeFalse();
        fixture.DbContext.ChangeTracker.Clear();
        archiveEntity = await fixture.DbContext.Archives.SingleAsync();
        archiveEntity.Status.ShouldBe(ArchiveStatus.Uninstalled);

        await installer.ReinstallArchiveAsync(archiveEntity.Id,
            new LoadedArchive(Path.Combine(fixture.BackupPath, "content.zip"))
            {
                ArchiveId = archiveEntity.Id
            }).ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
        fixture.DbContext.ChangeTracker.Clear();
        archiveEntity = await fixture.DbContext.Archives.SingleAsync();
        archiveEntity.Status.ShouldBe(ArchiveStatus.Installed);
        (await fixture.DbContext.Archives.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ReinstallArchiveAsync_ThrowsWhenBackupIsMissing()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archiveEntity = await fixture.DbContext.Archives.SingleAsync();
        await installer.UninstallArchiveAsync(archiveEntity.Id);

        File.Delete(Path.Combine(fixture.BackupPath, "content.zip"));

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in installer.ReinstallArchiveAsync(archiveEntity.Id,
                               new LoadedArchive(Path.Combine(fixture.BackupPath, "content.zip"))
                               {
                                   ArchiveId = archiveEntity.Id
                               }))
            {
            }
        });
    }

    [Fact]
    public async Task UninstallArchiveAsync_DeletesOnlyActiveFilesForArchive()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var firstArchive = fixture.CreateArchive("first.zip", ("data/shared/file.txt", "first"));
        var secondArchive = fixture.CreateArchive("second.zip", ("data/shared/file.txt", "second version"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(firstArchive)]).ToListAsync();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(secondArchive)]).ToListAsync();

        var firstArchiveEntity = await fixture.DbContext.Archives.SingleAsync(x => x.ArchiveName == "first.zip");
        await installer.UninstallArchiveAsync(firstArchiveEntity.Id);

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")).ShouldBeTrue();

        var secondArchiveEntity = await fixture.DbContext.Archives.SingleAsync(x => x.ArchiveName == "second.zip");
        await installer.UninstallArchiveAsync(secondArchiveEntity.Id);

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "file.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task InstallArchivesAsync_MarksIdenticalContentWithDifferentNameAsDuplicate()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var originalArchive = fixture.CreateArchive("original.zip", ("data/author/product/file.txt", "hello"));
        var renamedArchive = fixture.CreateArchive("renamed.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(originalArchive)])
            .ToListAsync();
        var duplicateResults = await installer
            .InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(renamedArchive)]).ToListAsync();

        duplicateResults.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Duplicate);
        (await fixture.DbContext.Archives.CountAsync()).ShouldBe(1);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task InstallArchivesAsync_StoresContentFingerprintOnInstalledArchive()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.Include(x => x.AssetFiles).SingleAsync();
        archive.ContentFingerprint.ShouldNotBeNullOrWhiteSpace();
        archive.ContentFingerprint.ShouldBe(
            ArchiveContentFingerprint.Compute([
                (Path.Combine("data", "author", "product", "file.txt"), archive.AssetFiles.Single().FileHash,
                    archive.AssetFiles.Single().FileSize)
            ]));
    }

    [Fact]
    public async Task InstallArchivesAsync_InstallsBundleAfterStandaloneAndSkipsSharedFiles()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var standaloneArchive = fixture.CreateArchive("standalone.zip", ("data/shared/shared.txt", "shared"));
        var extraArchive = fixture.CreateArchive("extra.zip", ("data/shared/extra.txt", "extra"));
        var bundleArchive = fixture.CreateArchiveWithFiles("bundle.zip",
        [
            ("standalone.zip", standaloneArchive),
            ("extra.zip", extraArchive)
        ]);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(standaloneArchive)])
            .ToListAsync();
        var bundleResults = await installer
            .InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(bundleArchive)]).ToListAsync();

        bundleResults.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Installed);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "shared.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "extra.txt")).ShouldBeTrue();

        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.AssetFile)
            .Include(x => x.Archive)
            .ToListAsync();

        records.Count(x =>
                x.Archive.ArchiveName == "standalone.zip" &&
                x.InstallRecordStatus == InstallRecordStatus.Installed)
            .ShouldBe(1);
        records.Count(x =>
                x.Archive.ArchiveName == "standalone.zip" &&
                x.InstallRecordStatus == InstallRecordStatus.Skipped)
            .ShouldBe(1);
        records.Count(x =>
                x.Archive.ArchiveName == "extra.zip" &&
                x.InstallRecordStatus == InstallRecordStatus.Installed)
            .ShouldBe(1);
    }

    [Fact]
    public async Task InstallArchivesAsync_InstallsStandaloneAfterBundleAndSkipsSharedFiles()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var standaloneArchive = fixture.CreateArchive("standalone.zip", ("data/shared/shared.txt", "shared"));
        var extraArchive = fixture.CreateArchive("extra.zip", ("data/shared/extra.txt", "extra"));
        var bundleArchive = fixture.CreateArchiveWithFiles("bundle.zip",
        [
            ("standalone.zip", standaloneArchive),
            ("extra.zip", extraArchive)
        ]);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(bundleArchive)]).ToListAsync();
        var standaloneResults = await installer
            .InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(standaloneArchive)]).ToListAsync();

        standaloneResults.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Installed);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "shared.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "extra.txt")).ShouldBeTrue();

        var records = await fixture.DbContext.InstallRecords
            .Include(x => x.Archive)
            .ToListAsync();

        records.Count(x =>
                x.Archive.ArchiveName == "standalone.zip" &&
                x.InstallRecordStatus == InstallRecordStatus.Skipped)
            .ShouldBe(1);
        records.Count(x =>
                x.Archive.ArchiveName == "standalone.zip" &&
                x.InstallRecordStatus == InstallRecordStatus.Installed)
            .ShouldBe(1);
    }

    [Fact]
    public async Task UninstallArchiveAsync_KeepsSharedFilesUntilLastOwnerRemoved_WhenStandaloneInstalledFirst()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var standaloneArchive = fixture.CreateArchive("standalone.zip", ("data/shared/shared.txt", "shared"));
        var extraArchive = fixture.CreateArchive("extra.zip", ("data/shared/extra.txt", "extra"));
        var bundleArchive = fixture.CreateArchiveWithFiles("bundle.zip",
        [
            ("standalone.zip", standaloneArchive),
            ("extra.zip", extraArchive)
        ]);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(standaloneArchive)])
            .ToListAsync();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(bundleArchive)]).ToListAsync();

        var standaloneEntity = await fixture.DbContext.Archives.SingleAsync(x =>
            x.ArchiveName == "standalone.zip" && x.ParentArchiveId == null);
        var bundleEntity = await fixture.DbContext.Archives.SingleAsync(x => x.ArchiveName == "bundle.zip");

        await installer.UninstallArchiveAsync(standaloneEntity.Id);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "shared.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "extra.txt")).ShouldBeTrue();

        await installer.UninstallArchiveAsync(bundleEntity.Id);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "shared.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "extra.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task UninstallArchiveAsync_KeepsSharedFilesUntilLastOwnerRemoved_WhenBundleInstalledFirst()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var standaloneArchive = fixture.CreateArchive("standalone.zip", ("data/shared/shared.txt", "shared"));
        var extraArchive = fixture.CreateArchive("extra.zip", ("data/shared/extra.txt", "extra"));
        var bundleArchive = fixture.CreateArchiveWithFiles("bundle.zip",
        [
            ("standalone.zip", standaloneArchive),
            ("extra.zip", extraArchive)
        ]);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(bundleArchive)]).ToListAsync();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(standaloneArchive)])
            .ToListAsync();

        var standaloneEntity = await fixture.DbContext.Archives.SingleAsync(x =>
            x.ArchiveName == "standalone.zip" && x.ParentArchiveId == null);
        var bundleEntity = await fixture.DbContext.Archives.SingleAsync(x => x.ArchiveName == "bundle.zip");

        await installer.UninstallArchiveAsync(bundleEntity.Id);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "shared.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "extra.txt")).ShouldBeFalse();

        await installer.UninstallArchiveAsync(standaloneEntity.Id);
        File.Exists(Path.Combine(fixture.LibraryPath, "data", "shared", "shared.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task UninstallArchiveAsync_CleansUpFailedInstallAndAllowsReinstall()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var entity = await fixture.DbContext.Archives.SingleAsync();
        entity.Status = ArchiveStatus.Loading;
        entity.ErrorMessage = "Simulated install crash";
        await fixture.DbContext.SaveChangesAsync();

        var installedPath = Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt");
        File.Exists(installedPath).ShouldBeTrue();

        await installer.UninstallArchiveAsync(entity.Id);

        fixture.DbContext.ChangeTracker.Clear();
        (await fixture.DbContext.Archives.SingleAsync()).Status.ShouldBe(ArchiveStatus.Uninstalled);
        File.Exists(installedPath).ShouldBeFalse();

        var queueItem = new LoadedArchive(Path.Combine(fixture.BackupPath, "content.zip"))
        {
            ArchiveId = entity.Id,
            ArchiveStatus = ArchiveStatus.Ready,
            StatusText = "Ready to reinstall"
        };
        var results = await installer.ReinstallArchiveAsync(entity.Id, queueItem).ToListAsync();

        results.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Installed);
        File.Exists(installedPath).ShouldBeTrue();
    }

    [Fact]
    public async Task ForgetArchiveAsync_RemovesStoredThumbnail()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip",
            ("cover.png", "thumbnail"),
            ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var root = await fixture.DbContext.Archives.SingleAsync(x => x.ParentArchiveId == null);
        var thumbnailPath = Path.Combine(fixture.ThumbnailPath, $"{root.Id}.png");
        File.Exists(thumbnailPath).ShouldBeTrue();

        await installer.UninstallArchiveAsync(root.Id);
        await installer.ForgetArchiveAsync(root.Id);

        File.Exists(thumbnailPath).ShouldBeFalse();
    }

    public sealed class InstallerFixture : IAsyncDisposable
    {
        private readonly string _rootPath;
        private readonly DbContextOptions<ApplicationDbContext> _dbContextOptions;

        private InstallerFixture(string rootPath, ApplicationDbContext dbContext,
            DbContextOptions<ApplicationDbContext> dbContextOptions,
            IDbContextFactory<ApplicationDbContext> dbContextFactory, SettingsService settingsService,
            InstallerConfig config,
            AssetLibrary assetLibrary)
        {
            _rootPath = rootPath;
            _dbContextOptions = dbContextOptions;
            DbContext = dbContext;
            DbContextFactory = dbContextFactory;
            SettingsService = settingsService;
            Config = config;
            AssetLibrary = assetLibrary;
            LibraryPath = assetLibrary.Path;
            BackupPath = config.ArchiveBackupPath;
            ThumbnailPath = config.ArchiveThumbnailPath;
        }

        public ApplicationDbContext DbContext { get; }
        public IDbContextFactory<ApplicationDbContext> DbContextFactory { get; }
        public SettingsService SettingsService { get; }
        public InstallerConfig Config { get; }
        public AssetLibrary AssetLibrary { get; }
        public string LibraryPath { get; }
        public string BackupPath { get; }
        public string ThumbnailPath { get; }

        public static async Task<InstallerFixture> CreateAsync(
            Action<InstallerConfig>? configure = null,
            Action<AppSettings>? configureSettings = null)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), $"DazContentInstallerTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);

            var libraryPath = Path.Combine(rootPath, "library");
            Directory.CreateDirectory(libraryPath);

            var config = new InstallerConfig { AppDataPath = Path.Combine(rootPath, "appdata") };
            configure?.Invoke(config);
            Directory.CreateDirectory(config.AppDataPath);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={config.DbPath}")
                .Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.MigrateAsync();

            var dbContextFactory = new TestDbContextFactory(options);
            var settingsService = new SettingsService(Options.Create(config), dbContextFactory);
            var appSettings = new AppSettings
            {
                CreateBackupBeforeInstall = true,
                DefaultArchiveBackupPath = config.ArchiveBackupPath,
                DefaultArchiveThumbnailPath = config.ArchiveThumbnailPath
            };
            configureSettings?.Invoke(appSettings);
            settingsService.UpdateSettings(appSettings);

            var assetLibrary = new AssetLibrary
            {
                Name = "Test Library",
                Path = libraryPath
            };

            dbContext.AssetLibraries.Add(assetLibrary);
            await dbContext.SaveChangesAsync();

            return new InstallerFixture(rootPath, dbContext, options, dbContextFactory, settingsService, config,
                assetLibrary);
        }

        public IDazArchiveInstaller CreateInstaller(IDazArchiveScanner? archiveScanner = null)
        {
            var directoryService = new DirectoryService(SettingsService.CurrentSettings);
            return new DazArchiveInstaller(
                DbContextFactory,
                SettingsService,
                Config,
                directoryService,
                archiveScanner ?? new DazArchiveScanner(directoryService),
                new DestinationPathLockRegistry());
        }

        public string CreateArchive(string fileName, params (string EntryPath, string Content)[] entries)
        {
            var archivePath = Path.Combine(_rootPath, fileName);
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            foreach (var (entryPath, content) in entries)
            {
                var entry = archive.CreateEntry(entryPath);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }

            return archivePath;
        }

        public string CreateArchiveWithFiles(string fileName, params (string EntryPath, string SourcePath)[] files)
        {
            var archivePath = Path.Combine(_rootPath, fileName);
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            foreach (var (entryPath, sourcePath) in files)
                archive.CreateEntryFromFile(sourcePath, entryPath);

            return archivePath;
        }

        public string CreateCompositeArchive(string fileName,
            (string EntryPath, string Content)[] textEntries,
            params (string EntryPath, string SourcePath)[] fileEntries)
        {
            var archivePath = Path.Combine(_rootPath, fileName);
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            foreach (var (entryPath, content) in textEntries)
            {
                var entry = archive.CreateEntry(entryPath);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }

            foreach (var (entryPath, sourcePath) in fileEntries)
                archive.CreateEntryFromFile(sourcePath, entryPath);

            return archivePath;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, true);
        }
    }

    internal sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}