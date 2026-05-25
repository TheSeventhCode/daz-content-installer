using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DazContentInstaller.Services;

namespace DazContentInstaller.ViewModels;

public partial class ArchiveOverridesWindowViewModel(
    IArchiveOverrideService archiveOverrideService,
    IFileDialogService fileDialogService) : ViewModelBase
{
    private Guid _rootArchiveId;
    private string _archiveDisplayName = string.Empty;

    public ObservableCollection<string> CandidateDirectories { get; } = [];
    public ObservableCollection<ArchiveOverrideItemViewModel> Overrides { get; } = [];

    [ObservableProperty] public partial string? SelectedDirectory { get; set; }

    [ObservableProperty] public partial string DirectorySearchText { get; set; } = string.Empty;

    [ObservableProperty] public partial ArchiveOverrideItemViewModel? SelectedOverride { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty] public partial bool HasChanges { get; set; }

    public string WindowTitle => string.IsNullOrWhiteSpace(_archiveDisplayName)
        ? "Archive Overrides"
        : $"Overrides — {_archiveDisplayName}";

    public ObservableCollection<string> FilteredCandidateDirectories { get; } = [];

    public event EventHandler<bool>? CloseRequested;

    public event Func<string, Task<bool>>? ConfirmDeleteRequested;

    public async Task InitializeAsync(Guid rootArchiveId, string archiveDisplayName)
    {
        _rootArchiveId = rootArchiveId;
        _archiveDisplayName = archiveDisplayName;
        OnPropertyChanged(nameof(WindowTitle));

        await ReloadAsync();
    }

    partial void OnDirectorySearchTextChanged(string value) => ApplyDirectoryFilter();

    [RelayCommand]
    private async Task BrowseOverrideFilesAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDirectory))
        {
            StatusText = "Select a target directory first.";
            return;
        }

        var sourceFiles = await fileDialogService.OpenFilesAsync("Select override file(s)", allowMultiple: true);
        if (sourceFiles.Count == 0)
            return;

        try
        {
            await archiveOverrideService.AddOverridesAsync(_rootArchiveId, SelectedDirectory, sourceFiles);
            HasChanges = true;
            await ReloadAsync();
            StatusText = sourceFiles.Count == 1
                ? "Override added"
                : $"{sourceFiles.Count} overrides added";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedOverrideAsync()
    {
        if (SelectedOverride is null)
            return;

        var message =
            $"Delete the {SelectedOverride.ModeLabel.ToLowerInvariant()} for \"{SelectedOverride.InstalledRelativePath}\"?\n\n" +
            (SelectedOverride.Mode == Database.ArchiveOverrideMode.Replacement
                ? "The original installed file will be restored."
                : "The added file will be removed from the library.");

        if (ConfirmDeleteRequested is not null && !await ConfirmDeleteRequested(message))
            return;

        try
        {
            await archiveOverrideService.DeleteOverrideAsync(SelectedOverride.Id);
            HasChanges = true;
            await ReloadAsync();
            StatusText = "Override removed";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, HasChanges);
    }

    private async Task ReloadAsync()
    {
        var directories = await archiveOverrideService.GetCandidateDirectoriesAsync(_rootArchiveId);
        var overrides = await archiveOverrideService.GetOverridesAsync(_rootArchiveId);

        CandidateDirectories.Clear();
        foreach (var directory in directories)
            CandidateDirectories.Add(directory);

        Overrides.Clear();
        foreach (var item in overrides.Select(MapOverride))
            Overrides.Add(item);

        SelectedDirectory ??= CandidateDirectories.FirstOrDefault();
        ApplyDirectoryFilter();
    }

    private void ApplyDirectoryFilter()
    {
        FilteredCandidateDirectories.Clear();
        var query = DirectorySearchText.Trim();

        foreach (var directory in CandidateDirectories.Where(x =>
                     string.IsNullOrWhiteSpace(query) ||
                     x.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredCandidateDirectories.Add(directory);
        }
    }

    private static ArchiveOverrideItemViewModel MapOverride(ArchiveOverrideInfo info) =>
        new()
        {
            Id = info.Id,
            InstalledRelativeDirectory = info.InstalledRelativeDirectory,
            FileName = info.FileName,
            InstalledRelativePath = info.InstalledRelativePath,
            Mode = info.Mode,
            CreatedAt = info.CreatedAt
        };
}
