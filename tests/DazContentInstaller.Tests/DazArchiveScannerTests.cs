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

    [Fact]
    public async Task ScanArchiveAsync_ClassifiesAnimationsFolder()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        var archivePath = fixture.CreateArchive("animations.zip",
            ("data/author/animations/walk/file.duf", "content"));

        var result = await scanner.ScanArchiveAsync(archivePath);

        result.Categories.ShouldContain("animations");
        result.AssetTypes.ShouldContain(AssetType.Animations);
        result.AssetTypes.ShouldNotContain(AssetType.Poses);
    }

    [Fact]
    public async Task ScanArchiveAsync_ClassifiesAniBlocksRoot()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        var archivePath = fixture.CreateArchive("aniblocks.zip",
            ("aniBlocks/author/product/file.duf", "content"));

        var result = await scanner.ScanArchiveAsync(archivePath);

        result.Categories.ShouldContain("animations");
        result.Categories.ShouldNotContain("aniBlocks");
        result.AssetTypes.ShouldContain(AssetType.Animations);
        result.AssetTypes.ShouldNotContain(AssetType.Poses);
    }

    [Theory]
    [InlineData("aniBlocks", "animations")]
    [InlineData("wardrobe", "clothing")]
    [InlineData("hair", "hair")]
    [InlineData("readme", "documentation")]
    public void NormalizeCategory_maps_aliases_to_canonical_name(string category, string expected)
    {
        DazArchiveScanner.NormalizeCategory(category).ShouldBe(expected);
    }

    [Theory]
    [InlineData("data/author/product/file.duf", "data", "author", "product", "file.duf", "")]
    [InlineData("content/data/author/product/file.duf", "data", "author", "product", "file.duf", "content")]
    [InlineData("aniBlocks/author/product/file.duf", "aniBlocks", "author", "product", "file.duf", "")]
    [InlineData("ReadMe/author/product/readme.pdf", "Documentation", "author", "product", "readme.pdf", "")]
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
    [InlineData("DATA/author/product/file.duf", "data/author/product/file.duf")]
    [InlineData("runtime/textures/a.jpg", "Runtime/Textures/a.jpg")]
    [InlineData("RUNTIME/TEXTURES/a.jpg", "Runtime/Textures/a.jpg")]
    [InlineData("ReadMe/author/readme.pdf", "Documentation/author/readme.pdf")]
    [InlineData("README/author/readme.pdf", "Documentation/author/readme.pdf")]
    public void CanonicalizeInstalledRelativePath_UsesDazDefaultCasing(string input, string expected)
    {
        DazArchiveScanner.CanonicalizeInstalledRelativePath(input).ShouldBe(expected);
    }

    [Fact]
    public void CanonicalizeInstalledRelativePath_PrefersExistingLibraryCasing()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"DazCanonicalPathTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(libraryRoot, "Data", "Author"));

        try
        {
            var canonical = DazArchiveScanner.CanonicalizeInstalledRelativePath(
                "data/author/product/file.txt", libraryRoot);

            canonical.ShouldBe(Path.Combine("Data", "Author", "product", "file.txt"));
        }
        finally
        {
            if (Directory.Exists(libraryRoot))
                Directory.Delete(libraryRoot, true);
        }
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

    [Fact]
    public async Task ScanArchiveAsync_MapsReadMeRootToDocumentation()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        var archivePath = fixture.CreateArchive("readme.zip",
            ("ReadMe/author/product/readme.pdf", "content"));

        var result = await scanner.ScanArchiveAsync(archivePath);

        result.InstallableFiles.Count.ShouldBe(1);
        result.InstallableFiles[0].InstalledRelativePath
            .ShouldBe(Path.Combine("Documentation", "author", "product", "readme.pdf"));
    }

    [Theory]
    [InlineData("data/author/product/file.duf", false)]
    public void ShouldIgnoreArchiveEntry_IgnoresKnownFiles(string path, bool expected)
    {
        DazArchiveScanner.ShouldIgnoreArchiveEntry(path).ShouldBe(expected);
    }

    [Fact]
    public async Task ScanArchiveAsync_ReadsDisplayNameFromSupplementDsx()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        const string supplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Business Presentation Poses"/>
            </ProductSupplement>
            """;
        var archivePath = fixture.CreateArchive("content.zip",
            ("Supplement.dsx", supplement),
            ("data/author/product/file.duf", "content"));

        var result = await scanner.ScanArchiveAsync(archivePath);

        result.DisplayName.ShouldBe("Business Presentation Poses");
        result.ArchiveName.ShouldBe("content.zip");
    }

    [Fact]
    public async Task ScanArchiveAsync_LeavesDisplayNameNullWhenSupplementDsxMissing()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        var archivePath = fixture.CreateArchive("content.zip",
            ("data/author/product/file.duf", "content"));

        var result = await scanner.ScanArchiveAsync(archivePath);

        result.DisplayName.ShouldBeNull();
    }

    [Fact]
    public async Task ScanArchiveAsync_ReadsDisplayNameFromNestedSupplementDsx()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        const string supplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Business Presentation Poses"/>
            </ProductSupplement>
            """;
        var innerArchivePath = fixture.CreateArchive("inner.zip",
            ("Supplement.dsx", supplement),
            ("data/author/product/file.duf", "content"));
        var outerArchivePath = fixture.CreateArchiveWithFiles("bundle.zip",
            ("IM0001-product.zip", innerArchivePath));

        var result = await scanner.ScanArchiveAsync(outerArchivePath);

        result.DisplayName.ShouldBeNull();
        result.ResolveRootDisplayName().ShouldBe("Business Presentation Poses");
        result.RootEffectiveDisplayName.ShouldBe("Business Presentation Poses");
        result.ContentBearingNestedArchives.Count.ShouldBe(1);
        result.ContentBearingNestedArchives[0].DisplayName.ShouldBe("Business Presentation Poses");
        result.ContentBearingNestedArchives[0].ArchivePath.ShouldBe("IM0001-product.zip");
    }

    [Fact]
    public async Task ScanArchiveAsync_DeduplicatesWhenOuterArchiveAlsoContainsDirectContent()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
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
        var outerArchivePath = fixture.CreateCompositeArchive("bundle.zip",
            [
                ("environments/author/bundle/scene.duf", "scene"),
                ("data/author/materials/bundle/surface.duf", "surface"),
                ("runtime/textures/author/bundle/texture.jpg", "texture")
            ],
            ("IM00054761-01_WinterblackSanctuaryFallenDS.zip", firstInner),
            ("IM00054761-02_WinterblackSanctuaryFallenPs.zip", secondInner));

        var result = await scanner.ScanArchiveAsync(outerArchivePath);

        result.AssetTypes.Count.ShouldBe(3);
        result.Categories.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ScanArchiveAsync_DeduplicatesAssetTypesFromNestedArchivesWithSimilarContent()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
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

        var result = await scanner.ScanArchiveAsync(outerArchivePath);

        result.AssetTypes.Count.ShouldBe(3);
        result.AssetTypes.ShouldBe([AssetType.Environment, AssetType.Materials, AssetType.Textures],
            ignoreOrder: true);
        result.Categories.Count.ShouldBe(3);
        result.Categories.ShouldBe(["environments", "materials", "textures"], ignoreOrder: true);
    }

    [Fact]
    public async Task ScanArchiveAsync_KeepsOuterDisplayNameWhenParentHasSupplementDsx()
    {
        await using var fixture = await DazArchiveInstallerTests.InstallerFixture.CreateAsync();
        var directoryService = new DirectoryService(fixture.SettingsService.CurrentSettings);
        var scanner = new DazArchiveScanner(directoryService);
        const string outerSupplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Outer Bundle"/>
            </ProductSupplement>
            """;
        const string innerSupplement = """
            <ProductSupplement VERSION="0.1">
              <ProductName VALUE="Inner Product"/>
            </ProductSupplement>
            """;
        var innerArchivePath = fixture.CreateArchive("inner.zip",
            ("Supplement.dsx", innerSupplement),
            ("data/author/product/file.duf", "content"));
        var outerArchivePath = fixture.CreateCompositeArchive("bundle.zip",
            [("Supplement.dsx", outerSupplement)],
            ("IM0001-product.zip", innerArchivePath));

        var result = await scanner.ScanArchiveAsync(outerArchivePath);

        result.DisplayName.ShouldBe("Outer Bundle");
        result.ContentBearingNestedArchives.Count.ShouldBe(1);
        result.ContentBearingNestedArchives[0].DisplayName.ShouldBe("Inner Product");
    }
}