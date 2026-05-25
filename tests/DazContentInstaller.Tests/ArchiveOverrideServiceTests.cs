using System.IO;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveOverrideServiceTests
{
    [Fact]
    public async Task AddOverridesAsync_AdditionCreatesManagedOverrideAndCopiesFile()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var overrideService = fixture.CreateArchiveOverrideService();
        var customFilePath = Path.Combine(fixture.Config.AppDataPath, "custom-texture.png");
        await File.WriteAllTextAsync(customFilePath, "custom texture");

        await overrideService.AddOverridesAsync(
            archive.Id,
            Path.Combine("data", "author", "product"),
            [customFilePath]);

        var destinationPath = Path.Combine(fixture.LibraryPath, "data", "author", "product", "custom-texture.png");
        File.Exists(destinationPath).ShouldBeTrue();
        (await File.ReadAllTextAsync(destinationPath)).ShouldBe("custom texture");

        var overrides = await overrideService.GetOverridesAsync(archive.Id);
        overrides.Count.ShouldBe(1);
        overrides[0].Mode.ShouldBe(ArchiveOverrideMode.Addition);
        overrides[0].FileName.ShouldBe("custom-texture.png");
        (await overrideService.HasOverridesAsync(archive.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task AddOverridesAsync_ReplacementBacksUpOriginalAndRestoresOnDelete()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "original"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var overrideService = fixture.CreateArchiveOverrideService();
        var customFilePath = Path.Combine(fixture.Config.AppDataPath, "file.txt");
        await File.WriteAllTextAsync(customFilePath, "custom");

        var installedDirectory = Path.Combine("data", "author", "product");
        await overrideService.AddOverridesAsync(archive.Id, installedDirectory, [customFilePath]);

        var destinationPath = Path.Combine(fixture.LibraryPath, installedDirectory, "file.txt");
        (await File.ReadAllTextAsync(destinationPath)).ShouldBe("custom");

        var overrides = await overrideService.GetOverridesAsync(archive.Id);
        overrides.Single().Mode.ShouldBe(ArchiveOverrideMode.Replacement);

        await overrideService.DeleteOverrideAsync(overrides[0].Id);

        (await File.ReadAllTextAsync(destinationPath)).ShouldBe("original");
        (await overrideService.GetOverridesAsync(archive.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteOverrideAsync_RemovesAdditionFromLibrary()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var overrideService = fixture.CreateArchiveOverrideService();
        var customFilePath = Path.Combine(fixture.Config.AppDataPath, "extra.txt");
        await File.WriteAllTextAsync(customFilePath, "extra");

        var installedDirectory = Path.Combine("data", "author", "product");
        await overrideService.AddOverridesAsync(archive.Id, installedDirectory, [customFilePath]);

        var destinationPath = Path.Combine(fixture.LibraryPath, installedDirectory, "extra.txt");
        File.Exists(destinationPath).ShouldBeTrue();

        var overrideId = (await overrideService.GetOverridesAsync(archive.Id)).Single().Id;
        await overrideService.DeleteOverrideAsync(overrideId);

        File.Exists(destinationPath).ShouldBeFalse();
        (await overrideService.HasOverridesAsync(archive.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task AddOverridesAsync_UsesCanonicalLibraryPathWhenDirectoryCasingDiffers()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        Directory.CreateDirectory(Path.Combine(fixture.LibraryPath, "Runtime", "Scripts"));

        var archivePath = fixture.CreateArchive("content.zip", ("runtime/scripts/file.txt", "hello"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var overrideService = fixture.CreateArchiveOverrideService();
        var customFilePath = Path.Combine(fixture.Config.AppDataPath, "custom-script.dse");
        await File.WriteAllTextAsync(customFilePath, "custom script");

        await overrideService.AddOverridesAsync(
            archive.Id,
            Path.Combine("runtime", "scripts"),
            [customFilePath]);

        var destinationPath = Path.Combine(fixture.LibraryPath, "Runtime", "Scripts", "custom-script.dse");
        File.Exists(destinationPath).ShouldBeTrue();
        (await File.ReadAllTextAsync(destinationPath)).ShouldBe("custom script");
    }

    [Fact]
    public async Task GetCandidateDirectoriesAsync_DoesNotIncludeArchiveContentRoot()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip",
            ("VendorPack/data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        archive.ContentRoot.ShouldBe("VendorPack");

        var overrideService = fixture.CreateArchiveOverrideService();
        var directories = await overrideService.GetCandidateDirectoriesAsync(archive.Id);

        directories.ShouldNotContain("VendorPack");
        directories.ShouldContain(Path.Combine("data", "author", "product"));
    }

    [Fact]
    public async Task UninstallArchiveAsync_IsBlockedWhileOverridesExist()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var overrideService = fixture.CreateArchiveOverrideService();
        var customFilePath = Path.Combine(fixture.Config.AppDataPath, "extra.txt");
        await File.WriteAllTextAsync(customFilePath, "extra");

        await overrideService.AddOverridesAsync(
            archive.Id,
            Path.Combine("data", "author", "product"),
            [customFilePath]);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            installer.UninstallArchiveAsync(archive.Id));
        exception.Message.ShouldContain("override");

        (await fixture.DbContext.Archives.SingleAsync()).Status.ShouldBe(ArchiveStatus.Installed);
    }

    [Fact]
    public async Task UninstallArchiveAsync_SucceedsAfterOverridesRemoved()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var archivePath = fixture.CreateArchive("content.zip", ("data/author/product/file.txt", "hello"));
        var installer = fixture.CreateInstaller();
        await installer.InstallArchivesAsync(fixture.AssetLibrary.Id, [new LoadedArchive(archivePath)]).ToListAsync();

        var archive = await fixture.DbContext.Archives.SingleAsync();
        var overrideService = fixture.CreateArchiveOverrideService();
        var customFilePath = Path.Combine(fixture.Config.AppDataPath, "extra.txt");
        await File.WriteAllTextAsync(customFilePath, "extra");

        await overrideService.AddOverridesAsync(
            archive.Id,
            Path.Combine("data", "author", "product"),
            [customFilePath]);

        var overrideId = (await overrideService.GetOverridesAsync(archive.Id)).Single().Id;
        await overrideService.DeleteOverrideAsync(overrideId);

        await installer.UninstallArchiveAsync(archive.Id);

        fixture.DbContext.ChangeTracker.Clear();
        (await fixture.DbContext.Archives.SingleAsync()).Status.ShouldBe(ArchiveStatus.Uninstalled);
    }
}
