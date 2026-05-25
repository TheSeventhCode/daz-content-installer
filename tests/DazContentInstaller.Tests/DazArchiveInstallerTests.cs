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

        var results = await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "data", "author", "product", "file.txt")).ShouldBeTrue();
        File.Exists(Path.Combine(fixture.BackupPath, "content.zip")).ShouldBeTrue();
        results.Last().ArchiveStatus.ShouldBe(ArchiveStatus.Installed);

        var archive = await fixture.DbContext.Archives.Include(x => x.AssetFiles).SingleAsync();
        archive.Status.ShouldBe(ArchiveStatus.Installed);
        archive.AssetFiles.Single().InstalledRelativePath.ShouldBe(Path.Combine("data", "author", "product", "file.txt"));
    }

    [Fact]
    public async Task InstallArchivesAsync_InstallsContentFromNestedArchives()
    {
        await using var fixture = await InstallerFixture.CreateAsync();
        var nestedArchivePath = fixture.CreateArchive("inner.zip", ("Runtime/Textures/thing.jpg", "image"));
        var outerArchivePath = fixture.CreateArchiveWithFiles("bundle.zip", [("IM0001-product.zip", nestedArchivePath)]);
        var installer = fixture.CreateInstaller();

        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(outerArchivePath)]).ToListAsync();

        File.Exists(Path.Combine(fixture.LibraryPath, "Runtime", "Textures", "thing.jpg")).ShouldBeTrue();

        var archives = await fixture.DbContext.Archives.ToListAsync();
        archives.Count.ShouldBe(2);
        archives.Count(x => x.ParentArchiveId is not null).ShouldBe(1);
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
        archive.AssetFiles.Single().InstalledRelativePath.ShouldBe(Path.Combine("data", "author", "product", "file.txt"));
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

        await installer.ReinstallArchiveAsync(archiveEntity.Id, new LoadedArchive(Path.Combine(fixture.BackupPath, "content.zip"))
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
            await foreach (var _ in installer.ReinstallArchiveAsync(archiveEntity.Id, new LoadedArchive(Path.Combine(fixture.BackupPath, "content.zip"))
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

    public sealed class InstallerFixture : IAsyncDisposable
    {
        private readonly string _rootPath;
        private readonly DbContextOptions<ApplicationDbContext> _dbContextOptions;

        private InstallerFixture(string rootPath, ApplicationDbContext dbContext, DbContextOptions<ApplicationDbContext> dbContextOptions,
            IDbContextFactory<ApplicationDbContext> dbContextFactory, SettingsService settingsService, InstallerConfig config,
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
        }

        public ApplicationDbContext DbContext { get; }
        public IDbContextFactory<ApplicationDbContext> DbContextFactory { get; }
        public SettingsService SettingsService { get; }
        public InstallerConfig Config { get; }
        public AssetLibrary AssetLibrary { get; }
        public string LibraryPath { get; }
        public string BackupPath { get; }

        public static async Task<InstallerFixture> CreateAsync(Action<InstallerConfig>? configure = null)
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
            settingsService.UpdateSettings(new AppSettings
            {
                CreateBackupBeforeInstall = true,
                DefaultArchiveBackupPath = config.ArchiveBackupPath
            });

            var assetLibrary = new AssetLibrary
            {
                Name = "Test Library",
                Path = libraryPath
            };

            dbContext.AssetLibraries.Add(assetLibrary);
            await dbContext.SaveChangesAsync();

            return new InstallerFixture(rootPath, dbContext, options, dbContextFactory, settingsService, config, assetLibrary);
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
