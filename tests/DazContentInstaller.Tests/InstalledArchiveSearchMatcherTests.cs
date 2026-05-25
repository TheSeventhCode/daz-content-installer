using DazContentInstaller.Database;
using DazContentInstaller.ViewModels;
using Shouldly;

namespace DazContentInstaller.Tests;

public class InstalledArchiveSearchMatcherTests
{
    [Fact]
    public void Matches_ReturnsTrueWhenDescendantArchiveNameMatches()
    {
        var archive = CreateTopLevel("top.zip", descendantArchiveName: "sub1.zip");

        InstalledArchiveSearchMatcher.Matches(archive, "sub1").ShouldBeTrue();
    }

    [Fact]
    public void Matches_ReturnsTrueWhenDescendantDisplayNameMatches()
    {
        var archive = CreateTopLevel(
            "top.zip",
            descendantArchiveName: "IM0001-product.zip",
            descendantDisplayName: "Business Poses");

        InstalledArchiveSearchMatcher.Matches(archive, "business").ShouldBeTrue();
    }

    [Fact]
    public void Matches_ReturnsFalseWhenOnlyUnrelatedDescendantMatches()
    {
        var archive = CreateTopLevel("top.zip", descendantArchiveName: "other.zip");

        InstalledArchiveSearchMatcher.Matches(archive, "sub1").ShouldBeFalse();
    }

    [Fact]
    public void Matches_StillMatchesTopLevelFields()
    {
        var archive = CreateTopLevel("bundle.zip", assetLibraryName: "Main Library");

        InstalledArchiveSearchMatcher.Matches(archive, "main library").ShouldBeTrue();
    }

    private static InstalledArchiveViewModel CreateTopLevel(
        string archiveName,
        string? displayName = null,
        string? assetLibraryName = null,
        string? descendantArchiveName = null,
        string? descendantDisplayName = null)
    {
        IReadOnlyList<InstalledArchiveSearchSegment> descendants = descendantArchiveName is null
            ? []
            :
            [
                new InstalledArchiveSearchSegment(
                    descendantArchiveName,
                    descendantDisplayName,
                    null,
                    [],
                    [])
            ];

        return new InstalledArchiveViewModel
        {
            ArchiveName = archiveName,
            DisplayName = displayName,
            AssetLibraryName = assetLibraryName ?? "Library",
            BackupPath = "/backup",
            Status = ArchiveStatus.Installed,
            DescendantSearchSegments = descendants
        };
    }
}
