using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using DynamicData;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;

namespace DazContentInstaller.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ApplicationDbContext _dbContext = null!;
    public ObservableCollection<LoadedArchive> LoadedArchives { get; set; } = [];

    private ObservableCollection<LoadedArchive> _selectedArchives = [];

    public ObservableCollection<LoadedArchive> SelectedArchives
    {
        get => _selectedArchives;
        set => SetProperty(ref _selectedArchives, value);
    }

    public ObservableCollection<AssetLibrary> AssetLibraries { get; set; } = [];
    private AssetLibrary? _currentSelectedAssetLibrary;

    public AssetLibrary? CurrentSelectedAssetLibrary
    {
        get => _currentSelectedAssetLibrary;
        set => SetProperty(ref _currentSelectedAssetLibrary, value);
    }

    private bool _installButtonEnabled;

    public bool InstallButtonEnabled
    {
        get => _installButtonEnabled;
        set => SetProperty(ref _installButtonEnabled, value);
    }

    private string _statusText = "Ready";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public MainWindowViewModel()
    {
        ClearLoadedArchivesCommand = ReactiveCommand.Create(ClearLoadedArchives);
        InstallArchivesCommand = ReactiveCommand.CreateFromTask(InstallArchives);
    }

    public MainWindowViewModel(ApplicationDbContext dbContext) : this()
    {
        _dbContext = dbContext;
    }

    private void ClearLoadedArchives()
    {
        LoadedArchives.Clear();
        UpdateInstallButton();
        StatusText = "Ready";
    }

    public ReactiveCommand<Unit, Unit> ClearLoadedArchivesCommand { get; set; }
    public ReactiveCommand<Unit, Unit> InstallArchivesCommand { get; set; }

    public async Task LoadAssetLibrariesAsync()
    {
        var libraries = await _dbContext.AssetLibraries.ToListAsync();
        AssetLibraries.Clear();
        AssetLibraries.AddRange(libraries);
        CurrentSelectedAssetLibrary = libraries.OrderByDescending(d => d.IsDefault).FirstOrDefault();
    }

    private async Task InstallArchives()
    {
        if (CurrentSelectedAssetLibrary is null)
            return;
        
        var archivesToInstall =
            SelectedArchives.Count > 0 ? SelectedArchives.AsEnumerable() : LoadedArchives.AsEnumerable();
        
        foreach (var archive in archivesToInstall)
        {
            using var installer = new DazArchiveInstaller(archive);
            await installer.InstallAsync(CurrentSelectedAssetLibrary.Path);
        }
    }

    public async Task LoadArchiveFileAsync(string filePath)
    {
        try
        {
            using var archive = new DazArchiveLoader(filePath);
            var result = await archive.LoadArchiveAsync();

            LoadedArchives.Add(result);
            UpdateInstallButton();
            StatusText = $"Loaded {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading {Path.GetFileName(filePath)}: {ex.Message}";
        }
    }

    private void UpdateInstallButton()
    {
        InstallButtonEnabled = LoadedArchives.Count > 0 && CurrentSelectedAssetLibrary != null;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}