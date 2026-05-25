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
}