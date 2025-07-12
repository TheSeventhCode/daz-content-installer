using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Mail;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using DazContentInstaller.Database;
using DazContentInstaller.Services;
using DynamicData;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;

namespace DazContentInstaller.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    public ObservableCollection<AssetLibrary> AssetLibraries { get; set; } = [];

    public readonly SettingsService SettingsService;
    private readonly ApplicationDbContext _dbContext;
    private bool _autoDetectDazLibraries = true;
    private bool _createBackupBeforeInstall = true;
    private string _tempDirectory = string.Empty;

    public bool AutoDetectDazLibraries
    {
        get => _autoDetectDazLibraries;
        set
        {
            SettingsService.CurrentSettings.AutoDetectDazLibraries = value;
            SetProperty(ref _autoDetectDazLibraries, value);
        }
    }

    public bool CreateBackupBeforeInstall
    {
        get => _createBackupBeforeInstall;
        set
        {
            SettingsService.CurrentSettings.CreateBackupBeforeInstall = value;
            SetProperty(ref _createBackupBeforeInstall, value);
        }
    }

    public string TempDirectory
    {
        get => _tempDirectory;
        set
        {
            SettingsService.CurrentSettings.TempDirectory = value;
            SetProperty(ref _tempDirectory, value);
        }
    }

    public SettingsWindowViewModel(SettingsService settingsService, ApplicationDbContext dbContext) : this()
    {
        SettingsService = settingsService;
        _dbContext = dbContext;
    }

    public SettingsWindowViewModel()
    {
        AutoDetectClick = ReactiveCommand.Create(AutoDetectLibrariesAsync);
    }
    
    public ReactiveCommand<Unit, Unit> AutoDetectClick { get; set; }
    
    private void AutoDetectLibrariesAsync()
    {
        SettingsService.AutoDetectDazLibrariesAsync().Wait();
        ReloadLibrariesAsync().Wait();
    }

    private async Task ReloadLibrariesAsync()
    {
        AssetLibraries.Clear();
        AssetLibraries.AddRange(await _dbContext.AssetLibraries.ToListAsync());
    }
}