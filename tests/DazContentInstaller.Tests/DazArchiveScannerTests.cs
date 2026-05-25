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

    [Theory]
    [InlineData("data/author/product/file.duf", "data", "author", "product", "file.duf", "")]
    [InlineData("content/data/author/product/file.duf", "data", "author", "product", "file.duf", "content")]
    public void TryGetInstalledRelativePath_DetectsContentRoot(string entryPath, string rootSegment,
        string segment2, string segment3, string segment4, string expectedContentRoot)
    {
        var expectedInstalledPath = Path.Combine(rootSegment, segment2, segment3, segment4);

        var found = DazArchiveScanner.TryGetInstalledRelativePath(entryPath, out var installedRelativePath,
            out var contentRoot);

        found.ShouldBeTrue();
        installedRelativePath.ShouldBe(expectedInstalledPath);
        contentRoot.ShouldBe(expectedContentRoot);
    }

    [Theory]
    [InlineData("\\data\\file.txt", "data/file.txt")]
    [InlineData("/Runtime/textures/a.jpg", "Runtime/textures/a.jpg")]
    public void NormalizeArchivePath_NormalizesSeparatorsAndLeadingSlashes(string input, string expected)
    {
        DazArchiveScanner.NormalizeArchivePath(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("nested/inner.zip", true)]
    [InlineData("nested/inner.txt", false)]
    public void IsNestedArchive_DetectsSupportedExtensions(string path, bool expected)
    {
        DazArchiveScanner.IsNestedArchive(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("data/author/Thumbs.db", true)]
    [InlineData("data/author/product/file.duf", false)]
    public void ShouldIgnoreArchiveEntry_IgnoresKnownFiles(string path, bool expected)
    {
        DazArchiveScanner.ShouldIgnoreArchiveEntry(path).ShouldBe(expected);
    }
}