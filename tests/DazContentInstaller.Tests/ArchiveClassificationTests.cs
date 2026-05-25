using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveClassificationTests
{
    [Fact]
    public void MergeAssetTypes_DeduplicatesNestedArchivesWithSimilarContent()
    {
        var scan = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            AssetTypes = [AssetType.Environment, AssetType.Materials, AssetType.Textures],
            Categories = ["environments", "materials", "textures"],
            NestedArchives =
            [
                new DazArchiveScanResult
                {
                    ArchiveName = "first.zip",
                    ArchivePath = "first.zip",
                    AssetTypes = [AssetType.Environment, AssetType.Materials, AssetType.Textures],
                    Categories = ["environments", "materials", "textures"]
                },
                new DazArchiveScanResult
                {
                    ArchiveName = "second.zip",
                    ArchivePath = "second.zip",
                    AssetTypes = [AssetType.Environment, AssetType.Materials, AssetType.Textures],
                    Categories = ["environments", "materials", "textures"]
                }
            ]
        };

        ArchiveClassification.MergeAssetTypes(scan).ShouldBe(
        [
            AssetType.Environment,
            AssetType.Materials,
            AssetType.Textures
        ]);

        ArchiveClassification.MergeCategories(scan).ShouldBe(["environments", "materials", "textures"]);
    }

    [Fact]
    public void ReplaceClassifications_RemovesDuplicateAssetTypesFromLoadedArchive()
    {
        var loaded = new LoadedArchive("/tmp/bundle.zip");
        var scan = new DazArchiveScanResult
        {
            ArchiveName = "bundle.zip",
            ArchivePath = "/tmp/bundle.zip",
            AssetTypes =
            [
                AssetType.Environment,
                AssetType.Materials,
                AssetType.Textures,
                AssetType.Environment,
                AssetType.Materials,
                AssetType.Textures
            ],
            Categories =
            [
                "environments",
                "materials",
                "textures",
                "environments",
                "materials",
                "textures"
            ]
        };

        loaded.ReplaceClassifications(scan);

        loaded.AssetTypes.Count.ShouldBe(3);
        loaded.Categories.Count.ShouldBe(3);
    }
}
