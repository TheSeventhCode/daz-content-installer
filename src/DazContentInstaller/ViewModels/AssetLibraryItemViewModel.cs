using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DazContentInstaller.ViewModels;

public partial class AssetLibraryItemViewModel : ViewModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string? _archiveBackupPath;

    [ObservableProperty]
    private bool _isDefault;

    partial void OnPathChanged(string value) => ApplyDefaultBackupPathIfUnset();

    public void ApplyDefaultBackupPathIfUnset()
    {
        if (string.IsNullOrWhiteSpace(Path) || !string.IsNullOrWhiteSpace(ArchiveBackupPath))
            return;

        ArchiveBackupPath = System.IO.Path.Combine(Path.Trim(), "backup");
    }
}
