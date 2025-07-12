using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ApplicationDbContext _dbContext;
    public ObservableCollection<LoadedArchive> LoadedArchives { get; set; } = [];
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
    }
    public MainWindowViewModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LoadAssetLibrariesAsync()
    {
        var libraries = await _dbContext.AssetLibraries.ToListAsync();
        AssetLibraries = new ObservableCollection<AssetLibrary>(libraries);
    }
    
    public void LoadArchiveFile(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var asset = new LoadedArchive
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Size = FormatFileSize(fileInfo.Length),
                Status = ArchiveStatus.Loaded
            };
                
            LoadedArchives.Add(asset);
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
