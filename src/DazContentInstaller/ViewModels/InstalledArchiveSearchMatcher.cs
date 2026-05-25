using System;
using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;

namespace DazContentInstaller.ViewModels;

public static class InstalledArchiveSearchMatcher
{
    public static bool Matches(InstalledArchiveViewModel archive, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return MatchesTopLevel(archive, query)
               || archive.DescendantSearchSegments.Any(segment => MatchesSegment(segment, query));
    }

    public static bool MatchesTopLevel(InstalledArchiveViewModel archive, string query)
    {
        return Contains(archive.EffectiveDisplayName, query)
               || Contains(archive.ArchiveName, query)
               || Contains(archive.DisplayName, query)
               || Contains(archive.AssetLibraryName, query)
               || Contains(archive.BackupPath, query)
               || Contains(archive.ContentRoot, query)
               || Contains(archive.Status.ToString(), query)
               || archive.Categories.Any(category => Contains(category, query))
               || archive.AssetTypes.Any(assetType => Contains(assetType.ToString(), query));
    }

    public static bool MatchesSegment(InstalledArchiveSearchSegment segment, string query)
    {
        return Contains(segment.EffectiveDisplayName, query)
               || Contains(segment.ArchiveName, query)
               || Contains(segment.DisplayName, query)
               || Contains(segment.ContentRoot, query)
               || segment.Categories.Any(category => Contains(category, query))
               || segment.AssetTypes.Any(assetType => Contains(assetType.ToString(), query));
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrEmpty(value)
               && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
