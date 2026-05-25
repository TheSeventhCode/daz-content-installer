using System;
using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;
using DazContentInstaller.Services;

namespace DazContentInstaller.Extensions;

public static class ArchiveClassification
{
    public static IReadOnlyList<AssetType> DistinctAssetTypes(IEnumerable<AssetType> assetTypes)
    {
        return assetTypes
            .Where(x => x != AssetType.Unknown)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    public static IReadOnlyList<string> DistinctCategories(IEnumerable<string> categories)
    {
        return categories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(DazArchiveScanner.NormalizeCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<AssetType> MergeAssetTypes(DazArchiveScanResult scan)
    {
        var assetTypes = new HashSet<AssetType>(scan.AssetTypes);

        foreach (var nested in scan.NestedArchives)
        {
            foreach (var assetType in MergeAssetTypes(nested))
                assetTypes.Add(assetType);
        }

        return assetTypes
            .Where(x => x != AssetType.Unknown)
            .OrderBy(x => x)
            .ToList();
    }

    public static IReadOnlyList<string> MergeCategories(DazArchiveScanResult scan)
    {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in scan.Categories)
            categories.Add(DazArchiveScanner.NormalizeCategory(category));

        foreach (var nested in scan.NestedArchives)
        {
            foreach (var category in MergeCategories(nested))
                categories.Add(category);
        }

        return categories
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
