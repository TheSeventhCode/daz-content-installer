using System;
using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.Services;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Models;

public sealed class NestedArchiveSummary
{
    public string ArchiveName { get; init; } = string.Empty;
    public string ArchivePath { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string EffectiveDisplayName => ArchiveDisplayName.GetEffectiveDisplayName(ArchiveName, DisplayName);
    public bool HasDistinctDisplayName => ArchiveDisplayName.HasDistinctDisplayName(ArchiveName, DisplayName);
    public int FileCount { get; init; }
    public ulong InstallableSize { get; init; }
    public IReadOnlyList<AssetType> AssetTypes { get; init; } = [];
    public IReadOnlyList<string> FilePaths { get; init; } = [];
    public string Summary => $"{FileCount:N0} file(s), {FileSizeFormatter.Format(InstallableSize)}";

    public static NestedArchiveSummary FromScanResult(DazArchiveScanResult scan)
    {
        return new NestedArchiveSummary
        {
            ArchiveName = scan.ArchiveName,
            ArchivePath = scan.ArchivePath,
            DisplayName = scan.DisplayName,
            FileCount = scan.InstallableFileCount,
            InstallableSize = scan.InstallableSize,
            AssetTypes = scan.AssetTypes,
            FilePaths = scan.GetAllInstallableFiles()
                .Select(x => x.InstalledRelativePath)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }
}
