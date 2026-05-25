using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;

namespace DazContentInstaller.Services;

public sealed class DazArchiveScanResult
{
    public required string ArchiveName { get; init; }
    public required string ArchivePath { get; init; }
    public string? DisplayName { get; init; }
    public ulong ArchiveSize { get; init; }
    public string? ContentRoot { get; init; }
    public IReadOnlyList<AssetType> AssetTypes { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<DazArchiveScanEntry> InstallableFiles { get; init; } = [];
    public IReadOnlyList<DazArchiveScanResult> NestedArchives { get; init; } = [];

    public int InstallableFileCount => InstallableFiles.Count + NestedArchives.Sum(x => x.InstallableFileCount);

    public ulong InstallableSize => InstallableFiles.Aggregate(0UL, (sum, file) => sum + file.FileSize)
                                    + NestedArchives.Aggregate(0UL, (sum, archive) => sum + archive.InstallableSize);
}

public sealed class DazArchiveScanEntry
{
    public required string ArchiveRelativePath { get; init; }
    public required string InstalledRelativePath { get; init; }
    public required ulong FileSize { get; init; }
}