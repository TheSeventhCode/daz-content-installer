using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.ViewModels;

public partial class SettingsWindowViewModel(
    ApplicationDbContext dbContext,
    SettingsService settingsService,
    IFileDialogService fileDialogService) : ViewModelBase
{
    public ObservableCollection<AssetLibraryItemViewModel> AssetLibraries { get; } = [];

    [ObservableProperty]
    private AssetLibraryItemViewModel? _selectedAssetLibrary;

    [ObservableProperty]
    private bool _autoDetectDazLibraries;

    [ObservableProperty]
    private bool _createBackupBeforeInstall;

    [ObservableProperty]
    private string? _defaultArchiveBackupPath;

    [ObservableProperty]
    private string? _tempWorkingDirectoryPath;

    [ObservableProperty]
    private string _statusText = "Ready";

    public event EventHandler<bool>? CloseRequested;

    public async Task InitializeAsync()
    {
        await settingsService.EnsureSettingsLoadedAsync();
        var settings = settingsService.CurrentSettings;
        AutoDetectDazLibraries = settings.AutoDetectDazLibraries;
        CreateBackupBeforeInstall = settings.CreateBackupBeforeInstall;
        DefaultArchiveBackupPath = settings.DefaultArchiveBackupPath;
        TempWorkingDirectoryPath = settings.TempWorkingDirectoryPath;

        var libraries = await dbContext.AssetLibraries.OrderBy(x => x.Name).ToListAsync();
        AssetLibraries.Clear();
        foreach (var library in libraries)
        {
            var libraryViewModel = new AssetLibraryItemViewModel
            {
                Id = library.Id,
                Name = library.Name,
                Path = library.Path,
                ArchiveBackupPath = library.ArchiveBackupPath,
                IsDefault = library.Id == settings.DefaultAssetLibrary
            };
            AssetLibraries.Add(libraryViewModel);
        }

        SortAssetLibraries(preserveSelection: false);
        SelectedAssetLibrary = AssetLibraries.FirstOrDefault(x => x.IsDefault) ?? AssetLibraries.FirstOrDefault();
    }

    [RelayCommand]
    private void AddAssetLibrary()
    {
        var library = new AssetLibraryItemViewModel
        {
            Name = "New Library",
            IsDefault = AssetLibraries.Count == 0
        };

        AssetLibraries.Add(library);
        SortAssetLibraries();
        SelectedAssetLibrary = library;
        StatusText = "Library added";
    }

    [RelayCommand]
    private void RemoveSelectedAssetLibrary()
    {
        if (SelectedAssetLibrary is null)
            return;

        var removedDefault = SelectedAssetLibrary.IsDefault;
        AssetLibraries.Remove(SelectedAssetLibrary);
        SelectedAssetLibrary = AssetLibraries.FirstOrDefault();

        if (removedDefault && SelectedAssetLibrary is not null)
            SelectedAssetLibrary.IsDefault = true;

        StatusText = "Library removed";
    }

    [RelayCommand]
    private async Task BrowseSelectedLibraryPathAsync()
    {
        if (SelectedAssetLibrary is null)
            return;

        var path = await fileDialogService.OpenFolderAsync("Select asset library", SelectedAssetLibrary.Path);
        if (!string.IsNullOrWhiteSpace(path))
        {
            SelectedAssetLibrary.Path = path;
            SelectedAssetLibrary.ApplyDefaultBackupPathIfUnset();
        }
    }

    [RelayCommand]
    private async Task BrowseSelectedLibraryBackupPathAsync()
    {
        if (SelectedAssetLibrary is null)
            return;

        var path = await fileDialogService.OpenFolderAsync("Select archive backup folder",
            SelectedAssetLibrary.ArchiveBackupPath);
        if (!string.IsNullOrWhiteSpace(path))
            SelectedAssetLibrary.ArchiveBackupPath = path;
    }

    [RelayCommand]
    private async Task BrowseDefaultArchiveBackupPathAsync()
    {
        var path = await fileDialogService.OpenFolderAsync("Select default archive backup folder",
            DefaultArchiveBackupPath);
        if (!string.IsNullOrWhiteSpace(path))
            DefaultArchiveBackupPath = path;
    }

    [RelayCommand]
    private async Task BrowseTempWorkingDirectoryPathAsync()
    {
        var path = await fileDialogService.OpenFolderAsync("Select temp working directory",
            TempWorkingDirectoryPath);
        if (!string.IsNullOrWhiteSpace(path))
            TempWorkingDirectoryPath = path;
    }

    [RelayCommand]
    private async Task AutoDetectLibrariesAsync()
    {
        await settingsService.AutoDetectDazLibrariesAsync();
        await InitializeAsync();
        StatusText = "Library auto-detection completed";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        NormalizeDefaultLibrarySelection();
        SortAssetLibraries();

        foreach (var library in AssetLibraries)
            library.ApplyDefaultBackupPathIfUnset();

        foreach (var library in AssetLibraries)
        {
            if (string.IsNullOrWhiteSpace(library.Name) || string.IsNullOrWhiteSpace(library.Path))
            {
                StatusText = "Each asset library requires a name and path.";
                return;
            }
        }

        var currentLibraries = await dbContext.AssetLibraries.ToListAsync();
        foreach (var currentLibrary in currentLibraries.Where(x => AssetLibraries.All(vm => vm.Id != x.Id)))
            dbContext.AssetLibraries.Remove(currentLibrary);

        foreach (var library in AssetLibraries)
        {
            var entity = currentLibraries.FirstOrDefault(x => x.Id == library.Id);
            if (entity is null)
            {
                entity = new AssetLibrary { Id = library.Id };
                dbContext.AssetLibraries.Add(entity);
            }

            entity.Name = library.Name.Trim();
            entity.Path = library.Path.Trim();
            entity.ArchiveBackupPath = string.IsNullOrWhiteSpace(library.ArchiveBackupPath)
                ? null
                : library.ArchiveBackupPath.Trim();
        }

        settingsService.UpdateSettings(new AppSettings
        {
            AutoDetectDazLibraries = AutoDetectDazLibraries,
            CreateBackupBeforeInstall = CreateBackupBeforeInstall,
            DefaultAssetLibrary = AssetLibraries.FirstOrDefault(x => x.IsDefault)?.Id ?? Guid.Empty,
            DefaultArchiveBackupPath = string.IsNullOrWhiteSpace(DefaultArchiveBackupPath) ? null : DefaultArchiveBackupPath.Trim(),
            TempWorkingDirectoryPath = string.IsNullOrWhiteSpace(TempWorkingDirectoryPath) ? null : TempWorkingDirectoryPath.Trim()
        });

        await dbContext.SaveChangesAsync();
        await settingsService.SaveSettingsAsync();

        StatusText = "Settings saved";
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    [RelayCommand]
    private void SetSelectedLibraryAsDefault()
    {
        if (SelectedAssetLibrary is null)
            return;

        foreach (var library in AssetLibraries)
            library.IsDefault = library == SelectedAssetLibrary;

        StatusText = $"{SelectedAssetLibrary.Name} set as default";
    }

    private void NormalizeDefaultLibrarySelection()
    {
        if (AssetLibraries.Count == 0)
            return;

        if (AssetLibraries.All(x => !x.IsDefault))
            AssetLibraries[0].IsDefault = true;

        var defaultLibrary = AssetLibraries.FirstOrDefault(x => x.IsDefault);
        if (defaultLibrary is null)
            return;

        foreach (var library in AssetLibraries.Where(x => x != defaultLibrary))
            library.IsDefault = false;
    }

    private void SortAssetLibraries(bool preserveSelection = true)
    {
        var selected = SelectedAssetLibrary;
        AssetLibraries.SortBy(x => x.Name);

        if (preserveSelection && selected is not null)
            SelectedAssetLibrary = selected;
    }
}