using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DazArchiveScanResultTests
{
    [Fact]
    public void InstallableFileCountAndSize_AggregateNestedArchives()
    {
        var result = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            InstallableFiles =
            [
                new DazArchiveScanEntry
                {
                    ArchiveRelativePath = "data/a/file.txt",
                    InstalledRelativePath = "data/a/file.txt",
                    FileSize = 10
                }
            ],
            NestedArchives =
            [
                new DazArchiveScanResult
                {
                    ArchiveName = "inner.zip",
                    ArchivePath = "inner.zip",
                    InstallableFiles =
                    [
                        new DazArchiveScanEntry
                        {
                            ArchiveRelativePath = "Runtime/textures/a.jpg",
                            InstalledRelativePath = "Runtime/textures/a.jpg",
                            FileSize = 25
                        },
                        new DazArchiveScanEntry
                        {
                            ArchiveRelativePath = "Runtime/textures/b.jpg",
                            InstalledRelativePath = "Runtime/textures/b.jpg",
                            FileSize = 15
                        }
                    ]
                }
            ]
        };

        result.InstallableFileCount.ShouldBe(3);
        result.InstallableSize.ShouldBe(50UL);
    }

    [Fact]
    public void ContentBearingNestedArchives_ExcludesEmptyNestedArchives()
    {
        var result = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            NestedArchives =
            [
                new DazArchiveScanResult
                {
                    ArchiveName = "empty.zip",
                    ArchivePath = "empty.zip"
                },
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

        result.ContentBearingNestedArchives.Count.ShouldBe(1);
        result.ContentBearingNestedArchives[0].DisplayName.ShouldBe("Business Poses");
    }

    [Fact]
    public void FindNestedScan_MatchesArchivePathCaseInsensitively()
    {
        var result = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            NestedArchives =
            [
                new DazArchiveScanResult
                {
                    ArchiveName = "product.zip",
                    ArchivePath = "folder/Product.ZIP",
                    DisplayName = "Business Poses"
                }
            ]
        };

        result.FindNestedScan("folder/product.zip")!.DisplayName.ShouldBe("Business Poses");
    }

    [Fact]
    public void ResolveRootDisplayName_PromotesSingleContentBearingChildDisplayName()
    {
        var result = new DazArchiveScanResult
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

        result.ResolveRootDisplayName().ShouldBe("Business Poses");
        result.RootEffectiveDisplayName.ShouldBe("Business Poses");
    }

    [Fact]
    public void ResolveRootDisplayName_PrefersOwnSupplementDsxOverSingleChild()
    {
        var result = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            DisplayName = "Outer Bundle",
            NestedArchives =
            [
                new DazArchiveScanResult
                {
                    ArchiveName = "product.zip",
                    ArchivePath = "product.zip",
                    DisplayName = "Inner Product",
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

        result.ResolveRootDisplayName().ShouldBe("Outer Bundle");
    }

    [Fact]
    public void ResolveRootDisplayName_DoesNotPromoteWhenMultipleContentBearingChildrenExist()
    {
        var result = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            NestedArchives =
            [
                new DazArchiveScanResult
                {
                    ArchiveName = "first.zip",
                    ArchivePath = "first.zip",
                    DisplayName = "First Product",
                    InstallableFiles =
                    [
                        new DazArchiveScanEntry
                        {
                            ArchiveRelativePath = "data/first/file.txt",
                            InstalledRelativePath = "data/first/file.txt",
                            FileSize = 10
                        }
                    ]
                },
                new DazArchiveScanResult
                {
                    ArchiveName = "second.zip",
                    ArchivePath = "second.zip",
                    DisplayName = "Second Product",
                    InstallableFiles =
                    [
                        new DazArchiveScanEntry
                        {
                            ArchiveRelativePath = "data/second/file.txt",
                            InstalledRelativePath = "data/second/file.txt",
                            FileSize = 10
                        }
                    ]
                }
            ]
        };

        result.ResolveRootDisplayName().ShouldBeNull();
        result.RootEffectiveDisplayName.ShouldBe("bundle.zip");
    }
}