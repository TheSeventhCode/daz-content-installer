using DazContentInstaller.ViewModels;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveFileGroupBuilderTests
{
    [Fact]
    public void Build_FlattensSingleSubArchiveIntoDirectFileList()
    {
        var subArchiveId = Guid.NewGuid();
        var groups = new List<ArchiveFileGroupViewModel>
        {
            new()
            {
                ArchiveId = subArchiveId,
                ArchiveName = "product.zip",
                DisplayName = "Business Poses",
                IsSubArchive = true,
                IsExpanded = false
            }
        };
        groups[0].Files.Add(new ArchiveFileRecordViewModel
        {
            ArchiveId = subArchiveId,
            OwnerArchiveName = "product.zip",
            OwnerDisplayName = "Business Poses",
            InstalledRelativePath = "data/a/file.txt"
        });

        var result = ArchiveFileGroupBuilder.Build(groups, Guid.NewGuid(), "bundle.zip", null);

        result.Count.ShouldBe(1);
        result[0].ShowFlatFiles.ShouldBeTrue();
        result[0].CanCollapse.ShouldBeFalse();
        result[0].Files.Count.ShouldBe(1);
    }

    [Fact]
    public void Build_KeepsMultipleSubArchiveGroupsSeparate()
    {
        var groups = new List<ArchiveFileGroupViewModel>
        {
            new()
            {
                ArchiveId = Guid.NewGuid(),
                ArchiveName = "first.zip",
                IsSubArchive = true
            },
            new()
            {
                ArchiveId = Guid.NewGuid(),
                ArchiveName = "second.zip",
                IsSubArchive = true
            }
        };

        var result = ArchiveFileGroupBuilder.Build(groups, Guid.NewGuid(), "bundle.zip", null);

        result.ShouldBeSameAs(groups);
        result.Count.ShouldBe(2);
    }
}
