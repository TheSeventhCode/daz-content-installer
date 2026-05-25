using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using DazContentInstaller.Database;

namespace DazContentInstaller.ViewModels;

public partial class InstalledArchiveViewModel : ViewModelBase
{
    public Guid Id { get; init; }
    public string ArchiveName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string EffectiveDisplayName => ArchiveDisplayName.GetEffectiveDisplayName(ArchiveName, DisplayName);
    public bool HasDistinctDisplayName => ArchiveDisplayName.HasDistinctDisplayName(ArchiveName, DisplayName);
    public string AssetLibraryName { get; init; } = string.Empty;
    public string? ContentRoot { get; init; }
    public string BackupPath { get; init; } = string.Empty;
    public ArchiveStatus Status { get; init; }
    public IReadOnlyList<AssetType> AssetTypes { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public DateTime? InstalledAt { get; init; }
    public int FileCount { get; init; }
    public int ActiveFileCount { get; init; }
    public int ReplacedFileCount { get; init; }
    public int SkippedFileCount { get; init; }
    public string? ErrorMessage { get; init; }
    public ulong ArchiveSize { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
