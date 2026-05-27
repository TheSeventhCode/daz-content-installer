using DazContentInstaller.ViewModels;
using Shouldly;

namespace DazContentInstaller.Tests;

public class InstalledArchiveSorterTests
{
    [Fact]
    public void Sort_OrdersByNameAscendingAndDescending()
    {
        var archives = new[]
        {
            CreateArchive("Bravo.zip", "Bravo", new DateTime(2026, 5, 27)),
            CreateArchive("alpha.zip", "alpha", new DateTime(2026, 5, 28)),
            CreateArchive("charlie.zip", "charlie", new DateTime(2026, 5, 26))
        };

        InstalledArchiveSorter.Sort(archives, InstalledArchiveSortMode.NameAscending)
            .Select(x => x.EffectiveDisplayName)
            .ShouldBe(["alpha", "Bravo", "charlie"]);

        InstalledArchiveSorter.Sort(archives, InstalledArchiveSortMode.NameDescending)
            .Select(x => x.EffectiveDisplayName)
            .ShouldBe(["charlie", "Bravo", "alpha"]);
    }

    [Fact]
    public void Sort_OrdersByInstalledTimeWithMissingDatesLast()
    {
        var archives = new[]
        {
            CreateArchive("old.zip", "Old", new DateTime(2026, 5, 25)),
            CreateArchive("unknown.zip", "Unknown", null),
            CreateArchive("new.zip", "New", new DateTime(2026, 5, 28))
        };

        InstalledArchiveSorter.Sort(archives, InstalledArchiveSortMode.InstalledNewest)
            .Select(x => x.EffectiveDisplayName)
            .ShouldBe(["New", "Old", "Unknown"]);

        InstalledArchiveSorter.Sort(archives, InstalledArchiveSortMode.InstalledOldest)
            .Select(x => x.EffectiveDisplayName)
            .ShouldBe(["Old", "New", "Unknown"]);
    }

    private static InstalledArchiveViewModel CreateArchive(string archiveName, string displayName, DateTime? installedAt)
    {
        return new InstalledArchiveViewModel
        {
            Id = Guid.NewGuid(),
            ArchiveName = archiveName,
            DisplayName = displayName,
            InstalledAt = installedAt
        };
    }
}
