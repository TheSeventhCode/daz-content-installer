using System;
using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;

namespace DazContentInstaller.ViewModels;

public sealed record InstalledArchiveSearchSegment(
    string ArchiveName,
    string? DisplayName,
    string? ContentRoot,
    IReadOnlyList<string> Categories,
    IReadOnlyList<AssetType> AssetTypes)
{
    public string EffectiveDisplayName => ArchiveDisplayName.GetEffectiveDisplayName(ArchiveName, DisplayName);

    public static InstalledArchiveSearchSegment FromArchive(Archive archive)
    {
        return new InstalledArchiveSearchSegment(
            archive.ArchiveName,
            archive.DisplayName,
            archive.ContentRoot,
            ParseCategories(archive.Categories),
            AssetTypeCollection.Parse(archive.AssetTypes));
    }

    private static List<string> ParseCategories(string categories)
    {
        if (string.IsNullOrWhiteSpace(categories))
            return [];

        return categories
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
