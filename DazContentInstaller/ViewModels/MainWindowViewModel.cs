using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
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
    private readonly ApplicationDbContext _dbContext = null!;
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

    public bool AllowArchiveLoad
    {
        get => _allowArchiveLoad;
        set => SetProperty(ref _allowArchiveLoad, value);
    }

    public bool UninstallButtonEnabled => SelectedInstallNodes.Any() && SelectedInstallNodes.All(d => d.Parent is null);

    private string _statusText = "Ready";
    private int _installedAssetsCount;
    private string _installedAssetsSearch = string.Empty;
    private int _statusProgress;
    private int _statusBarMax = 100;
    private IImmutableSolidColorBrush _statusBarColor = Brushes.DodgerBlue;
    private bool _allowArchiveLoad;

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

    public MainWindowViewModel(ApplicationDbContext dbContext,
        SettingsService settingsService) : this()
    {
        _dbContext = dbContext;
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
        AssetLibraries.Clear();
        await Task.Run(async () =>
        {
            await foreach (var library in _dbContext.AssetLibraries.AsAsyncEnumerable())
                AssetLibraries.Add(library);
        });

        CurrentSelectedAssetLibrary = AssetLibraries.OrderByDescending(d => d.IsDefault).FirstOrDefault();
        AllowArchiveLoad = true;
    }

    public async Task LoadInstalledArchivesAsync()
    {
        InstalledArchivesTree.Clear();
        DisplayedInstalledArchives.Clear();
        InstalledAssetsCount = 0;

        await Task.Run(async () =>
        {
            var archivesQuery = _dbContext.Archives
                .Where(d => d.Status == ArchiveStatus.Installed);
            if (CurrentSelectedAssetLibrary is not null)
                archivesQuery = archivesQuery.Where(d => d.AssetLibraryId == CurrentSelectedAssetLibrary.Id);

            await foreach (var archive in archivesQuery.OrderBy(d => d.ArchiveName.ToLower()).AsAsyncEnumerable())
            {
                var node = InstalledArchivesTree.LoadArchiveLazy(archive);
                DisplayedInstalledArchives.Add(node);

                InstalledAssetsCount++;
            }
        });
    }

    public async Task LoadArchiveTreeFilesAsync(TreeNode archiveNode)
    {
        if (!archiveNode.IsLazyLoad || archiveNode.HasLoadedChildren || archiveNode.DbId == null)
            return;

        try
        {
            StatusText = $"Loading files for {archiveNode.Title}...";
            StatusBarColor = Brushes.DodgerBlue;

            await Task.Run(async () =>
            {
                var archive = await _dbContext.Archives
                    .Include(a => a.AssetFiles)
                    .FirstOrDefaultAsync(a => a.Id == archiveNode.DbId);

                if (archive != null)
                {
                    Dispatcher.UIThread.Post(() => { InstalledArchiveTree.LoadArchiveFiles(archiveNode, archive); });
                }
            });

            StatusText = "Ready";
            StatusBarColor = Brushes.Green;
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading files: {ex.Message}";
            StatusBarColor = Brushes.Red;
        }
    }

    public async Task LoadArchiveFilesFromDiskAsync(List<string> filePaths)
    {
        StatusBarMax = 100 * filePaths.Count;
        StatusProgress = 0;
        StatusBarColor = Brushes.DodgerBlue;
        AllowArchiveLoad = false;

        var index = 0;

        await Task.Run(async () =>
        {
            foreach (var path in filePaths)
            {
                index++;
                var existingStatusProgress = index * 100;
                IProgress<string> messageProgress = new Progress<string>(s => StatusText = s);
                var percentageProgress = new Progress<int>(p => StatusProgress = existingStatusProgress + p);

                using var loader = new DazArchiveLoader(path);
                var result = await loader.LoadArchiveAsync(messageProgress, percentageProgress);

                Dispatcher.UIThread.Post(() =>
                {
                    LoadedArchives.AddRange(result.Where(d => d.ContainedFiles.Count > 0));
                    UpdateInstallButton();
                });
                messageProgress.Report($"Finished loading {Path.GetFileName(path)}");
            }
        });

        StatusBarMax = 100;
        StatusBarColor = Brushes.Green;
        AllowArchiveLoad = true;
    }

    private void RemoveLoadedArchive(LoadedArchive loadedArchiveOld)
    {
        LoadedArchives.Remove(loadedArchiveOld);
    }

    private async Task InstallArchives()
    {
        if (CurrentSelectedAssetLibrary is null)
            return;

        AllowArchiveLoad = false;
        var archivesToInstall =
            SelectedArchives.Count > 0 ? SelectedArchives.ToList() : LoadedArchives.ToList();

        IProgress<string> messageProgress = new Progress<string>(s => StatusText = s);
        IProgress<double> percentageProgress = new Progress<double>(s => StatusProgress = (int)s);
        StatusProgress = 0;
        StatusBarColor = Brushes.DodgerBlue;

        await Task.Run(async () =>
        {
            var archivesToInstallNames = archivesToInstall.Select(GetLoadedArchiveName).ToArray();
            var existingArchives = await _dbContext.Archives
                .Where(a => a.AssetLibraryId == CurrentSelectedAssetLibrary.Id &&
                            archivesToInstallNames.Contains(a.ArchiveName))
                .Include(a => a.AssetFiles)
                .ToListAsync();

            existingArchives = existingArchives.Where(e => archivesToInstall.Any(a =>
                    a.ContainedFiles.Count == e.AssetFiles.Count && GetLoadedArchiveName(a).Equals(e.ArchiveName)))
                .ToList();

            var loadedArchivesToSkip = archivesToInstall
                .IntersectBy(existingArchives.Select(d => d.ArchiveName), GetLoadedArchiveName).ToList();
            loadedArchivesToSkip.ForEach(d => d.ArchiveStatus = ArchiveStatus.Duplicate);

            archivesToInstall = archivesToInstall.Except(loadedArchivesToSkip).ToList();
            using var installer = new DazArchiveInstaller(archivesToInstall, _settingsService.CurrentSettings);
            await foreach (var archive in installer.InstallArchivesAsync(CurrentSelectedAssetLibrary.Path,
                               messageProgress,
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
                _dbContext.Archives.Add(dbArchive);
                await _dbContext.SaveChangesAsync();

                Dispatcher.UIThread.Post(() => LoadedArchives.Remove(archive));
            }
        });

        await Task.Run(async () => { await LoadInstalledArchivesAsync(); });

        messageProgress.Report($"Installed {archivesToInstall.Count} archives");
        percentageProgress.Report(100);
        StatusBarColor = Brushes.Green;
        AllowArchiveLoad = true;
    }

    private static string GetLoadedArchiveName(LoadedArchive archiveOld) =>
        archiveOld.Metadata.TryGetValue("ProductName", out var productName)
            ? productName.ToString()!
            : archiveOld.Name;

    private async Task UninstallArchiveAsync()
    {
        if (!SelectedInstallNodes.Any())
            return;

        AllowArchiveLoad = false;

        var selectedInstallArchiveIds = SelectedInstallNodes.Select(n => n.DbId).ToArray();
        var archives = await _dbContext.Archives
            .Include(a => a.AssetLibrary)
            .Include(a => a.AssetFiles)
            .Where(a => selectedInstallArchiveIds.Contains(a.Id))
            .ToListAsync();

        if (archives.Count < 1)
            return;

        await Task.Run(async () =>
        {
            IProgress<string> messageProgress = new Progress<string>(s => StatusText = s);
            IProgress<double> percentageProgress = new Progress<double>(s => StatusProgress = (int)s);
            StatusBarColor = Brushes.DodgerBlue;
            StatusProgress = 0;

            messageProgress.Report($"Reading {selectedInstallArchiveIds.Length} archives to uninstall...");

            var archiveIds = archives.Select(a => a.Id).ToArray();
            var deleteFileExceptions = await _dbContext.AssetFiles
                .Where(f => !archiveIds.Contains(f.ArchiveId) && f.InstalledPath != null)
                .ToListAsync();

            deleteFileExceptions = deleteFileExceptions
                .Where(e => archives.SelectMany(a => a.AssetFiles)
                    .Any(f => f.InstalledPath!.Equals(e.InstalledPath, StringComparison.OrdinalIgnoreCase)))
                .Distinct().ToList();

            var increment = Math.Ceiling(100D / archives.Count);

            var index = 0;

            foreach (var archive in archives)
            {
                index++;
                var uninstaller = new DazArchiveUninstaller(archive);
                await uninstaller.UninstallArchiveAsync(deleteFileExceptions.Select(d => d.InstalledPath!).ToHashSet());

                _dbContext.Archives.Remove(archive);
                await _dbContext.SaveChangesAsync();

                messageProgress.Report($"Uninstalled {archive.ArchiveName}");
                percentageProgress.Report(index * increment);
                await Task.Yield();
            }

            messageProgress.Report($"Uninstalled {archives.Count} archives");
            percentageProgress.Report(100);
            StatusBarColor = Brushes.Green;
        });

        await Task.Run(async () => { await LoadInstalledArchivesAsync(); });

        AllowArchiveLoad = true;
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
            .Where(node => node.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Select(node =>
                node is { IsLazyLoad: true, HasLoadedChildren: false } ? node : FilterTree(node, searchTerm))
            .Where(node => node is not null)
            .Select(node => node!);

        DisplayedInstalledArchives.AddRange(filtered);
    }

    private static TreeNode? FilterTree(TreeNode node, string searchTerm)
    {
        var filteredChildren = node.Children
            .Select(child => FilterTree(child, searchTerm))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToList();

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
        var archive = await _dbContext.Archives.FindAsync(selectedItem.DbId);
        if (archive is null)
            return;

        var files = await _dbContext.AssetFiles.Where(d => d.ArchiveId == archive.Id).ToListAsync();
        var details = new List<string>
        {
            $"File: {Path.GetFileName(archive.ArchiveName)}",
            $"Size: {FileSizeFormatter.FormatFileSize(files.Sum(f => (long)f.FileSize))}",
            $"Files: {files.Count:N0}"
        };

        SelectedInstalledAssetDetails = string.Join(" | ", details);
    }
}