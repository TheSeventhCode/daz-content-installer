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
    public partial bool IsDefault { get; set; }

    partial void OnPathChanged(string value) => ApplyDefaultBackupPathIfUnset();

    public void ApplyDefaultBackupPathIfUnset()
    {
        if (string.IsNullOrWhiteSpace(Path) || !string.IsNullOrWhiteSpace(ArchiveBackupPath))
            return;

        ArchiveBackupPath = System.IO.Path.Combine(Path.Trim(), "backup");
    }
}