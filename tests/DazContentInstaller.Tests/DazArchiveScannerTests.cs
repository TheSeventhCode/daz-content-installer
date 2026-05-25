using System.IO.Compression;
using DazContentInstaller.Database;
using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DazArchiveScannerTests
{
    [Fact]
    public async Task ScanArchiveAsync_DoesNotClassifyChairFolderAsHair()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        var archivePath = fixture.CreateArchive("chair.zip",
            ("data/author/MyChair/product/file.duf", "content"));

        var result = await scanner.ScanArchiveAsync(archivePath);

        result.Categories.ShouldNotContain("hair");
        result.AssetTypes.ShouldNotContain(AssetType.Hair);
    }

    [Fact]
    public async Task ScanArchiveAsync_ClassifiesHairFolderExactly()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        var archivePath = fixture.CreateArchive("hair.zip",
            ("data/author/hair/long/style.duf", "content"));

        var result = await scanner.ScanArchiveAsync(archivePath);

        result.Categories.ShouldContain("hair");
        result.AssetTypes.ShouldContain(AssetType.Hair);
    }
}
