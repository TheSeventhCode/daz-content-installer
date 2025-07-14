using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using DynamicData;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;

namespace DazContentInstaller.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory = null!;
    private readonly SettingsService _settingsService = null!;
    public ObservableCollection<LoadedArchive> LoadedArchives { get; set; } = [];
    private InstalledArchiveTree InstalledArchivesTree { get; } = [];
    public ObservableCollection<TreeNode> DisplayedInstalledArchives { get; } = [];

    private ObservableCollection<LoadedArchive> _selectedArchives = [];
    private ObservableCollection<TreeNode> _selectedInstallNodes = [];

    public ObservableCollection<LoadedArchive> SelectedArchives
    {
        get => _selectedArchives;
        set => SetProperty(ref _selectedArchives, value);
    }

    public ObservableCollection<TreeNode> SelectedInstallNodes
    {
        get => _selectedInstallNodes;
        set
        {
            SetProperty(ref _selectedInstallNodes, value);
            OnPropertyChanged(nameof(UninstallButtonEnabled));
        }
    }

    public ObservableCollection<AssetLibrary> AssetLibraries { get; set; } = [];
    private AssetLibrary? _currentSelectedAssetLibrary;

    public AssetLibrary? CurrentSelectedAssetLibrary
    {
        get => _currentSelectedAssetLibrary;
        set => SetProperty(ref _currentSelectedAssetLibrary, value);
    }

    private string? _selectedInstalledAssetDetails;

    public string? SelectedInstalledAssetDetails
    {
        get => _selectedInstalledAssetDetails ?? "Selected an asset to see details";
        set => SetProperty(ref _selectedInstalledAssetDetails, value);
    }

    private bool _installButtonEnabled;

    public bool InstallButtonEnabled
    {
        get => _installButtonEnabled;
        set => SetProperty(ref _installButtonEnabled, value);
    }

    public string InstalledAssetsSearch
    {
        get => _installedAssetsSearch;
        set
        {
            SetProperty(ref _installedAssetsSearch, value);
            FilterInstalledAssetsTree(value);
        }
    }

    public bool UninstallButtonEnabled => SelectedInstallNodes.Any() && SelectedInstallNodes.All(d => d.Parent is null);

    private string _statusText = "Ready";
    private int _installedAssetsCount;
    private string _installedAssetsSearch = string.Empty;
    private int _statusProgress;
    private int _statusBarMax = 100;
    private IImmutableSolidColorBrush _statusBarColor = Brushes.DodgerBlue;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public int StatusProgress
    {
        get => _statusProgress;
        set => SetProperty(ref _statusProgress, value);
    }

    public IImmutableSolidColorBrush StatusBarColor
    {
        get => _statusBarColor;
        set => SetProperty(ref _statusBarColor, value);
    }

    public int StatusBarMax
    {
        get => _statusBarMax;
        set => SetProperty(ref _statusBarMax, value);
    }

    private int InstalledAssetsCount
    {
        get => _installedAssetsCount;
        set
        {
            SetProperty(ref _installedAssetsCount, value);
            OnPropertyChanged(nameof(InstalledAssetsCountText));
        }
    }

    public string InstalledAssetsCountText => $"{InstalledAssetsCount} assets installed";

    public MainWindowViewModel()
    {
        ClearLoadedArchivesCommand = ReactiveCommand.Create(ClearLoadedArchives);
        InstallArchivesCommand = ReactiveCommand.CreateFromTask(InstallArchives);
        UninstallArchiveCommand = ReactiveCommand.CreateFromTask(UninstallArchiveAsync);
        RefreshInstalledAssets = ReactiveCommand.CreateFromTask(LoadInstalledArchivesAsync);
        RemoveLoadedArchiveClick = ReactiveCommand.Create<LoadedArchive>(RemoveLoadedArchive);
    }

    public MainWindowViewModel(IDbContextFactory<ApplicationDbContext> dbContextFactory,
        SettingsService settingsService) : this()
    {
        _dbContextFactory = dbContextFactory;
        _settingsService = settingsService;
    }

    private void ClearLoadedArchives()
    {
        LoadedArchives.Clear();
        UpdateInstallButton();
        StatusText = "Ready";
    }

    public ReactiveCommand<Unit, Unit> ClearLoadedArchivesCommand { get; set; }
    public ReactiveCommand<Unit, Unit> RefreshInstalledAssets { get; set; }
    public ReactiveCommand<Unit, Unit> InstallArchivesCommand { get; set; }
    public ReactiveCommand<Unit, Unit> UninstallArchiveCommand { get; set; }
    public ReactiveCommand<LoadedArchive, Unit> RemoveLoadedArchiveClick { get; set; }

    public async Task LoadAssetLibrariesAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var libraries = await dbContext.AssetLibraries.ToListAsync();
        AssetLibraries.Clear();
        AssetLibraries.AddRange(libraries);
        CurrentSelectedAssetLibrary = libraries.OrderByDescending(d => d.IsDefault).FirstOrDefault();
    }

    public async Task LoadInstalledArchivesAsync()
    {
        InstalledArchivesTree.Clear();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var archivesQuery = dbContext.Archives
            .Where(d => d.Status == ArchiveStatus.Installed);

        if (CurrentSelectedAssetLibrary is not null)
            archivesQuery = archivesQuery.Where(d => d.AssetLibraryId == CurrentSelectedAssetLibrary.Id);

        var archives = await archivesQuery
            .Include(d => d.AssetFiles)
            .OrderBy(d => d.ArchiveName)
            .ToListAsync();

        InstalledAssetsCount = archives.Count;
        foreach (var archive in archives)
            InstalledArchivesTree.LoadArchive(archive);

        FilterInstalledAssetsTree(InstalledAssetsSearch);
    }

    private void RemoveLoadedArchive(LoadedArchive loadedArchiveOld)
    {
        LoadedArchives.Remove(loadedArchiveOld);
    }

    private async Task InstallArchives()
    {
        if (CurrentSelectedAssetLibrary is null)
            return;

        var archivesToInstall =
            SelectedArchives.Count > 0 ? SelectedArchives.ToList() : LoadedArchives.ToList();

        StatusProgress = 0;
        IProgress<string> messageProgress = new Progress<string>(s => StatusText = s);
        StatusBarColor = Brushes.DodgerBlue;
        IProgress<double> percentageProgress = new Progress<double>(s => StatusProgress = (int)s);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var archivesToInstallNames = archivesToInstall.Select(GetLoadedArchiveName).ToArray();
        var existingArchives = await dbContext.Archives
            .Where(a => archivesToInstallNames.Contains(a.ArchiveName))
            .Include(a => a.AssetFiles)
            .ToListAsync();

        existingArchives = existingArchives.Where(e => archivesToInstall.Any(a =>
            a.ContainedFiles.Count == e.AssetFiles.Count && GetLoadedArchiveName(a).Equals(e.ArchiveName))).ToList();

        var loadedArchivesToSkip = archivesToInstall
            .IntersectBy(existingArchives.Select(d => d.ArchiveName), GetLoadedArchiveName).ToList();
        loadedArchivesToSkip.ForEach(d => d.ArchiveStatus = ArchiveStatus.Duplicate);

        archivesToInstall = archivesToInstall.Except(loadedArchivesToSkip).ToList();
        using var installer = new DazArchiveInstaller(archivesToInstall, _settingsService.CurrentSettings);

        await foreach (var archive in installer.InstallArchivesAsync(CurrentSelectedAssetLibrary.Path, messageProgress,
                           percentageProgress))
        {
            var dbArchive = new Archive
            {
                ArchiveName = archive.Name,
                ArchiveSize = archive.FileSizeBytes,
                Status = ArchiveStatus.Installed,
                CustomAssetsBasePath = archive.CustomAssetBaseDirectory,
                AssetLibraryId = CurrentSelectedAssetLibrary.Id
            };

            dbArchive.AssetFiles.AddRange(archive.ContainedFiles);
            dbContext.Archives.Add(dbArchive);
            await dbContext.SaveChangesAsync();

            LoadedArchives.Remove(archive);
            await LoadInstalledArchivesAsync();
        }

        messageProgress.Report($"Installed {archivesToInstall.Count} archives");
        percentageProgress.Report(100);
        StatusBarColor = Brushes.Green;
    }

    private static string GetLoadedArchiveName(LoadedArchive archiveOld) =>
        archiveOld.Metadata.TryGetValue("ProductName", out var productName)
            ? productName.ToString()!
            : archiveOld.Name;

    private async Task UninstallArchiveAsync()
    {
        if (!SelectedInstallNodes.Any())
            return;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var selectedInstallArchiveIds = SelectedInstallNodes.Select(n => n.DbId).ToArray();
        var archives = await dbContext.Archives
            .Include(a => a.AssetLibrary)
            .Include(a => a.AssetFiles)
            .Where(a => selectedInstallArchiveIds.Contains(a.Id))
            .ToListAsync();

        if (archives.Count < 1)
            return;

        var archiveIds = archives.Select(a => a.Id).ToArray();
        var deleteFileExceptions = await dbContext.AssetFiles
            .Where(f => !archiveIds.Contains(f.ArchiveId) && f.InstalledPath != null)
            .ToListAsync();

        deleteFileExceptions = deleteFileExceptions
            .Where(e => archives.SelectMany(a => a.AssetFiles)
                .Any(f => f.InstalledPath!.Equals(e.InstalledPath, StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToList();

        IProgress<string> messageProgress = new Progress<string>(s => StatusText = s);
        IProgress<double> percentageProgress = new Progress<double>(s => StatusProgress = (int)s);
        StatusBarColor = Brushes.DodgerBlue;
        StatusProgress = 0;
        var increment = Math.Ceiling(100D / archives.Count);

        var index = 0;
        foreach (var archive in archives)
        {
            index++;
            var uninstaller = new DazArchiveUninstaller(archive);
            await uninstaller.UninstallArchiveAsync(deleteFileExceptions.Select(d => d.InstalledPath!).ToHashSet());

            dbContext.Archives.Remove(archive);
            await dbContext.SaveChangesAsync();

            messageProgress.Report($"Uninstalled {archive.ArchiveName}");
            percentageProgress.Report(index * increment);
            await Task.Yield();
        }

        await LoadInstalledArchivesAsync();
        messageProgress.Report($"Uninstalled {archives.Count} archives");
        percentageProgress.Report(100);
        StatusBarColor = Brushes.Green;
    }

    public async Task LoadArchiveFilesAsync(List<string> filePaths)
    {
        StatusBarMax = 100 * filePaths.Count;
        StatusProgress = 0;
        StatusBarColor = Brushes.DodgerBlue;

        var index = 0;
        foreach (var path in filePaths)
        {
            index++;
            var existingStatusProgress = index * 100;
            IProgress<string> messageProgress = new Progress<string>(s => StatusText = s);
            var percentageProgress = new Progress<int>(p => StatusProgress = existingStatusProgress + p);

            using var loader = new DazArchiveLoader(path);
            var result = await loader.LoadArchiveAsync(messageProgress, percentageProgress);

            LoadedArchives.AddRange(result.Where(d => d.ContainedFiles.Count > 0));
            UpdateInstallButton();
            messageProgress.Report($"Finished loading {Path.GetFileName(path)}");
        }

        StatusBarMax = 100;
        StatusBarColor = Brushes.Green;
    }

    private void FilterInstalledAssetsTree(string? searchTerm)
    {
        DisplayedInstalledArchives.Clear();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            DisplayedInstalledArchives.AddRange(InstalledArchivesTree);
            return;
        }

        var filtered = InstalledArchivesTree
            .Select(node => FilterTree(node, searchTerm))
            .Where(node => node is not null)
            .Select(node => node!);

        DisplayedInstalledArchives.AddRange(filtered);
    }

    private static TreeNode? FilterTree(TreeNode node, string searchTerm)
    {
        // Filter children recursively
        var filteredChildren = node.Children
            .Select(child => FilterTree(child, searchTerm))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToList();

        // Check if this node matches or has any matching children
        var isMatch = node.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

        if (isMatch || filteredChildren.Count > 0)
            return new TreeNode(node.Title, node.DbId, filteredChildren, node.Parent);

        return null;
    }

    private void UpdateInstallButton()
    {
        InstallButtonEnabled = LoadedArchives.Count > 0 && CurrentSelectedAssetLibrary != null;
    }

    public async Task UpdateInstalledAssetDetailsAsync(TreeNode? selectedItem)
    {
        if (selectedItem is null)
            return;

        OnPropertyChanged(nameof(UninstallButtonEnabled));
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var archive = await dbContext.Archives.FindAsync(selectedItem.DbId);
        if (archive is null)
            return;

        var files = await dbContext.AssetFiles.Where(d => d.ArchiveId == archive.Id).ToListAsync();
        var details = new List<string>
        {
            $"File: {Path.GetFileName(archive.ArchiveName)}",
            $"Size: {FileSizeFormatter.FormatFileSize(files.Sum(f => (long)f.FileSize))}",
            $"Files: {files.Count:N0}"
        };

        SelectedInstalledAssetDetails = string.Join(" | ", details);
    }
}