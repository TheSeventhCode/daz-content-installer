using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DazContentInstaller.ViewModels;

public partial class ArchiveFileGroupViewModel : ViewModelBase
{
    public Guid ArchiveId { get; init; }
    public string ArchiveName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string EffectiveDisplayName => ArchiveDisplayName.GetEffectiveDisplayName(ArchiveName, DisplayName);
    public bool HasDistinctDisplayName => ArchiveDisplayName.HasDistinctDisplayName(ArchiveName, DisplayName);
    public bool IsSubArchive { get; init; }
    public bool ShowGroupHeader { get; init; } = true;
    public bool CanCollapse => IsSubArchive && ShowGroupHeader;
    public bool ShowRootGroupHeader => IsRootGroup && ShowGroupHeader;
    public bool ShowFlatFiles => !ShowGroupHeader;
    public bool IsRootGroup => !IsSubArchive;
    public ObservableCollection<ArchiveFileRecordViewModel> Files { get; } = [];
    public ObservableCollection<ArchiveFileRecordViewModel> FilteredFiles { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public int FileCount => FilteredFiles.Count;

    public string FileCountSummary => FileCount == 1 ? "1 file" : $"{FileCount:N0} files";

    public void RefreshFilterPresentation()
    {
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(FileCountSummary));
    }
}
