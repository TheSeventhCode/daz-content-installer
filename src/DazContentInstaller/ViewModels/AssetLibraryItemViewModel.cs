using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DazContentInstaller.ViewModels;

public partial class AssetLibraryItemViewModel : ViewModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Path { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ArchiveBackupPath { get; set; }

    [ObservableProperty]
    public partial string? ArchiveThumbnailPath { get; set; }

    [ObservableProperty]
    public partial bool IsDefault { get; set; }

    partial void OnPathChanged(string value) => ApplyDefaultPathsIfUnset();

    public void ApplyDefaultPathsIfUnset()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return;

        var trimmedPath = Path.Trim();
        if (string.IsNullOrWhiteSpace(ArchiveBackupPath))
            ArchiveBackupPath = System.IO.Path.Combine(trimmedPath, "backup");
        if (string.IsNullOrWhiteSpace(ArchiveThumbnailPath))
            ArchiveThumbnailPath = System.IO.Path.Combine(trimmedPath, "thumbnails");
    }
}