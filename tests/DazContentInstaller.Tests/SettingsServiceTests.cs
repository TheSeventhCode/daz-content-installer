using System.Text.Json;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DazContentInstaller.Tests;

public class SettingsServiceTests
{
    [Fact]
    public async Task EnsureSettingsLoadedAsync_CreatesDefaultSettingsWhenMissing()
    {
        await using var context = await CreateContextAsync();
        var service = context.Service;

        await service.EnsureSettingsLoadedAsync();

        File.Exists(context.Config.AppSettingsPath).ShouldBeTrue();
        service.CurrentSettings.DefaultArchiveBackupPath.ShouldBe(context.Config.ArchiveBackupPath);
        service.CurrentSettings.DefaultArchiveThumbnailPath.ShouldBe(context.Config.ArchiveThumbnailPath);
    }

    [Fact]
    public async Task EnsureSettingsLoadedAsync_LoadsExistingSettings()
    {
        await using var context = await CreateContextAsync();
        var expected = new AppSettings
        {
            CreateBackupBeforeInstall = false,
            DefaultArchiveBackupPath = Path.Combine(context.RootPath, "custom-backup"),
            DefaultArchiveThumbnailPath = Path.Combine(context.RootPath, "custom-thumbnails")
        };
        Directory.CreateDirectory(context.Config.AppDataPath);
        await File.WriteAllTextAsync(context.Config.AppSettingsPath, JsonSerializer.Serialize(expected));

        await context.Service.EnsureSettingsLoadedAsync();

        context.Service.CurrentSettings.CreateBackupBeforeInstall.ShouldBeFalse();
        context.Service.CurrentSettings.DefaultArchiveBackupPath.ShouldBe(expected.DefaultArchiveBackupPath);
        context.Service.CurrentSettings.DefaultArchiveThumbnailPath.ShouldBe(expected.DefaultArchiveThumbnailPath);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsCurrentSettings()
    {
        await using var context = await CreateContextAsync();
        context.Service.UpdateSettings(new AppSettings
        {
            CreateBackupBeforeInstall = false,
            DefaultArchiveBackupPath = Path.Combine(context.RootPath, "saved-backup"),
            DefaultArchiveThumbnailPath = Path.Combine(context.RootPath, "saved-thumbnails")
        });

        await context.Service.SaveSettingsAsync();

        var json = await File.ReadAllTextAsync(context.Config.AppSettingsPath);
        var saved = JsonSerializer.Deserialize<AppSettings>(json);
        saved.ShouldNotBeNull();
        saved!.CreateBackupBeforeInstall.ShouldBeFalse();
        saved.DefaultArchiveBackupPath.ShouldBe(Path.Combine(context.RootPath, "saved-backup"));
        saved.DefaultArchiveThumbnailPath.ShouldBe(Path.Combine(context.RootPath, "saved-thumbnails"));
    }

    [Fact]
    public async Task EnsureSettingsLoadedAsync_RecoversFromInvalidSettingsFile()
    {
        await using var context = await CreateContextAsync();
        Directory.CreateDirectory(context.Config.AppDataPath);
        await File.WriteAllTextAsync(context.Config.AppSettingsPath, "{ not valid json");

        await context.Service.EnsureSettingsLoadedAsync();

        context.Service.CurrentSettings.DefaultArchiveBackupPath.ShouldBe(context.Config.ArchiveBackupPath);
        context.Service.CurrentSettings.DefaultArchiveThumbnailPath.ShouldBe(context.Config.ArchiveThumbnailPath);
    }

    private static async Task<TestContext> CreateContextAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"DazContentInstallerSettingsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);

        var config = new InstallerConfig { AppDataPath = Path.Combine(rootPath, "appdata") };
        Directory.CreateDirectory(config.AppDataPath);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={config.DbPath}")
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.MigrateAsync();

        var service = new SettingsService(Options.Create(config), new TestDbContextFactory(options));
        return new TestContext(rootPath, config, service);
    }

    private sealed class TestContext(string rootPath, InstallerConfig config, SettingsService service)
        : IAsyncDisposable
    {
        public string RootPath { get; } = rootPath;
        public InstallerConfig Config { get; } = config;
        public SettingsService Service { get; } = service;

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}