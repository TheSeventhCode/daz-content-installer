using System;
using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;

namespace DazContentInstaller.Services;

public sealed class DazArchiveScanResult
{
    public required string ArchiveName { get; init; }
    public required string ArchivePath { get; init; }
    public string? DisplayName { get; init; }
    public string? ThumbnailArchiveRelativePath { get; init; }
    public ulong ArchiveSize { get; init; }
    public string? ContentRoot { get; init; }
    public IReadOnlyList<AssetType> AssetTypes { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<DazArchiveScanEntry> InstallableFiles { get; init; } = [];
    public IReadOnlyList<DazArchiveScanResult> NestedArchives { get; init; } = [];

    public int InstallableFileCount => InstallableFiles.Count + NestedArchives.Sum(x => x.InstallableFileCount);

    public ulong InstallableSize => InstallableFiles.Aggregate(0UL, (sum, file) => sum + file.FileSize)
                                    + NestedArchives.Aggregate(0UL, (sum, archive) => sum + archive.InstallableSize);

    public bool HasDirectInstallableContent => InstallableFiles.Count > 0;

    public bool HasInstallableContent => InstallableFileCount > 0;

    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? ArchiveName : DisplayName;

    public string? ResolveRootDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(DisplayName))
            return DisplayName;

        var contentBearingChildren = ContentBearingNestedArchives;
        if (contentBearingChildren.Count != 1)
            return null;

        var childDisplayName = contentBearingChildren[0].DisplayName;
        return string.IsNullOrWhiteSpace(childDisplayName) ? null : childDisplayName;
    }

    public string RootEffectiveDisplayName => ResolveRootDisplayName() ?? ArchiveName;

    public IReadOnlyList<DazArchiveScanResult> ContentBearingNestedArchives =>
        NestedArchives.Where(x => x.HasInstallableContent).ToList();

    public IEnumerable<DazArchiveScanEntry> GetAllInstallableFiles()
    {
        foreach (var file in InstallableFiles)
            yield return file;

        foreach (var nested in NestedArchives)
        {
            foreach (var file in nested.GetAllInstallableFiles())
                yield return file;
        }
    }

    public DazArchiveScanResult? FindNestedScan(string normalizedEntryPath)
    {
        return NestedArchives.FirstOrDefault(x =>
            string.Equals(x.ArchivePath, normalizedEntryPath, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class DazArchiveScanEntry
{
    public required string ArchiveRelativePath { get; init; }
    public required string InstalledRelativePath { get; init; }
    public required ulong FileSize { get; init; }
}
