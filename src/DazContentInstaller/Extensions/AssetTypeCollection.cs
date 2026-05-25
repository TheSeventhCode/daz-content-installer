using System;
using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;

namespace DazContentInstaller.Extensions;

public static class AssetTypeCollection
{
    public static string Serialize(IEnumerable<AssetType> assetTypes)
    {
        return string.Join(", ",
            assetTypes
                .Where(x => x != AssetType.Unknown)
                .Distinct()
                .OrderBy(x => x));
    }

    public static IReadOnlyList<AssetType> Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Enum.TryParse<AssetType>(x, out var assetType) ? assetType : AssetType.Unknown)
            .Where(x => x != AssetType.Unknown)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }
}
