using DazContentInstaller.ViewModels;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveDisplayNameTests
{
    [Fact]
    public void GetEffectiveDisplayName_FallsBackToArchiveName()
    {
        ArchiveDisplayName.GetEffectiveDisplayName("content.zip", null).ShouldBe("content.zip");
        ArchiveDisplayName.GetEffectiveDisplayName("content.zip", "   ").ShouldBe("content.zip");
    }

    [Fact]
    public void GetEffectiveDisplayName_UsesDisplayNameWhenPresent()
    {
        ArchiveDisplayName.GetEffectiveDisplayName("content.zip", "Business Poses")
            .ShouldBe("Business Poses");
    }

    [Theory]
    [InlineData("content.zip", "Business Poses", true)]
    [InlineData("content.zip", "content.zip", false)]
    [InlineData("content.zip", null, false)]
    public void HasDistinctDisplayName_DetectsDifferentLabels(string archiveName, string? displayName, bool expected)
    {
        ArchiveDisplayName.HasDistinctDisplayName(archiveName, displayName).ShouldBe(expected);
    }
}
