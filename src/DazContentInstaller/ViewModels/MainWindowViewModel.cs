using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.ViewModels;

public partial class MainWindowViewModel(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IDazArchiveInstaller archiveInstaller,
    IDazArchiveScanner archiveScanner,
    IFileDialogService fileDialogService,
    SettingsService settingsService,
    InstallerConfig installerConfig,
    IAssetLibraryStatisticsService assetLibraryStatisticsService)
    : ViewModelBase
{
    public ObservableCollection<LoadedArchive> QueueArchives { get; } = [];
    private ObservableCollection<InstalledArchiveViewModel> InstalledArchives { get; } = [];
    public ObservableCollection<InstalledArchiveViewModel> FilteredInstalledArchives { get; } = [];
    public ObservableCollection<InstalledArchiveViewModel> SelectedInstalledArchives { get; } = [];
    public ObservableCollection<LoadedArchive> SelectedQueueArchives { get; } = [];
    public ObservableCollection<AssetLibraryItemViewModel> AssetLibraries { get; } = [];
    private ObservableCollection<ArchiveFileRecordViewModel> SelectedArchiveFiles { get; } = [];
    public ObservableCollection<ArchiveFileRecordViewModel> FilteredSelectedArchiveFiles { get; } = [];

    private bool _selectionHandlersRegistered;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveQueuedArchivesCommand))]
    private partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty] public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallQueueCommand))]
    public partial AssetLibraryItemViewModel? SelectedAssetLibrary { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetSelectedArchiveCommand))]
    public partial InstalledArchiveViewModel? SelectedInstalledArchive { get; set; }

    [ObservableProperty] public partial LoadedArchive? SelectedQueueArchive { get; set; }

    [ObservableProperty] public partial string InstalledArchiveSearchText { get; set; } = string.Empty;

    [ObservableProperty] public partial string ArchiveFileSearchText { get; set; } = string.Empty;

    [ObservableProperty] private partial int SelectedAssetLibraryArchiveCount { get; set; }

    [ObservableProperty] private partial int SelectedAssetLibraryInstalledArchiveCount { get; set; }

    [ObservableProperty] private partial int SelectedAssetLibraryFileCount { get; set; }

    [ObservableProperty] private partial int SelectedAssetLibraryActiveFileCount { get; set; }

    [ObservableProperty] private partial ulong SelectedAssetLibraryTotalArchiveSize { get; set; }

    [ObservableProperty] private partial ulong SelectedAssetLibraryTotalContentSize { get; set; }

    public string ArchiveFileListSummary =>
        string.IsNullOrWhiteSpace(ArchiveFileSearchText)
            ? $"{SelectedArchiveFiles.Count} entries"
            : $"{FilteredSelectedArchiveFiles.Count} of {SelectedArchiveFiles.Count} entries";

    public string SelectedAssetLibrarySummary
    {
        get
        {
            if (SelectedAssetLibrary is null)
                return "No library selected";

            if (SelectedAssetLibraryArchiveCount == 0)
                return $"{SelectedAssetLibrary.Name} · no archives tracked";

            return string.Join(" · ",
            [
                SelectedAssetLibrary.Name,
                SelectedAssetLibraryArchiveCount == SelectedAssetLibraryInstalledArchiveCount
                    ? $"{SelectedAssetLibraryArchiveCount} archives"
                    : $"{SelectedAssetLibraryArchiveCount} archives ({SelectedAssetLibraryInstalledArchiveCount} installed)",
                $"{SelectedAssetLibraryFileCount:N0} files",
                $"{SelectedAssetLibraryActiveFileCount:N0} active",
                $"{FileSizeFormatter.Format(SelectedAssetLibraryTotalArchiveSize)} archives",
                $"{FileSizeFormatter.Format(SelectedAssetLibraryTotalContentSize)} installed"
            ]);
        }
    }

    public async Task InitializeAsync()
    {
        RegisterSelectionHandlers();
        await settingsService.EnsureSettingsLoadedAsync();
        await LoadLibrariesAsync();
        await RefreshInstalledArchivesAsync();
    }

    private void RegisterSelectionHandlers()
    {
        if (_selectionHandlersRegistered)
            return;

        _selectionHandlersRegistered = true;
        SelectedInstalledArchives.CollectionChanged += (_, _) =>
        {
            UninstallSelectedArchiveCommand.NotifyCanExecuteChanged();
            ReinstallSelectedArchiveCommand.NotifyCanExecuteChanged();
            ForgetSelectedArchiveCommand.NotifyCanExecuteChanged();
        };
        SelectedQueueArchives.CollectionChanged += (_, _) =>
            RemoveQueuedArchivesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedInstalledArchiveChanged(InstalledArchiveViewModel? value)
    {
        ArchiveFileSearchText = string.Empty;
        _ = LoadSelectedArchiveDetailsAsync(value);
    }

    partial void OnInstalledArchiveSearchTextChanged(string value) => ApplyInstalledArchiveFilter();

    partial void OnArchiveFileSearchTextChanged(string value)
    {
        ApplyArchiveFileFilter();
        OnPropertyChanged(nameof(ArchiveFileListSummary));
    }

    partial void OnSelectedAssetLibraryChanged(AssetLibraryItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));
        _ = RefreshInstalledArchivesAsync();
    }

    partial void OnSelectedAssetLibraryArchiveCountChanged(int value) =>
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));

    partial void OnSelectedAssetLibraryInstalledArchiveCountChanged(int value) =>
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));

    partial void OnSelectedAssetLibraryFileCountChanged(int value) =>
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));

    partial void OnSelectedAssetLibraryActiveFileCountChanged(int value) =>
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));

    partial void OnSelectedAssetLibraryTotalArchiveSizeChanged(ulong value) =>
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));

    partial void OnSelectedAssetLibraryTotalContentSizeChanged(ulong value) =>
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));

    [RelayCommand]
    private async Task AddArchivesAsync()
    {
        var paths = await fileDialogService.OpenArchiveFilesAsync();
        var addedArchives = new List<LoadedArchive>();
        foreach (var path in paths.Where(path => QueueArchives.All(existing =>
                     !string.Equals(existing.ArchivePath, path, StringComparison.OrdinalIgnoreCase))))
        {
            var archive = new LoadedArchive(path);
            QueueArchives.Add(archive);
            addedArchives.Add(archive);
        }

        SortQueueArchives();
        InstallQueueCommand.NotifyCanExecuteChanged();
        StatusText = QueueArchives.Count == 0 ? "No archives selected" : $"{QueueArchives.Count} archive(s) queued";

        if (addedArchives.Count == 0)
            return;

        IsBusy = true;
        ProgressPercent = 0;
        var total = addedArchives.Count;
        var completedScans = 0;
        var scanLock = new object();
        var maxScanParallelism = Math.Max(1, installerConfig.MaxConcurrentScans);
        using var scanSemaphore = new SemaphoreSlim(maxScanParallelism, maxScanParallelism);

        try
        {
            var scanTasks = addedArchives.Select(async archive =>
            {
                await scanSemaphore.WaitAsync();
                try
                {
                    await UiThread.RunAsync(() =>
                    {
                        archive.ArchiveStatus = ArchiveStatus.Loading;
                        archive.StatusText = "Scanning archive";
                        archive.ProgressPercent = 0;
                    });

                    try
                    {
                        var scan = await archiveScanner.ScanArchiveAsync(archive.ArchivePath);
                        await UiThread.RunAsync(() => archive.ApplyScan(scan));
                    }
                    catch (Exception ex)
                    {
                        await UiThread.RunAsync(() =>
                        {
                            archive.ArchiveStatus = ArchiveStatus.Error;
                            archive.ErrorMessage = ex.Message;
                            archive.StatusText = ex.Message;
                        });
                    }
                }
                finally
                {
                    int done;
                    lock (scanLock)
                    {
                        completedScans++;
                        done = completedScans;
                    }

                    var (percent, status) = QueueProgressAggregator.ComputeScanProgress(done, total);
                    await UiThread.RunAsync(() =>
                    {
                        ProgressPercent = percent;
                        StatusText = status;
                    });

                    scanSemaphore.Release();
                }
            });

            await Task.WhenAll(scanTasks);
        }
        finally
        {
            IsBusy = false;
            InstallQueueCommand.NotifyCanExecuteChanged();
            StatusText = $"{QueueArchives.Count} archive(s) queued";
            ProgressPercent = 0;
            SortQueueArchives();
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallQueue))]
    private async Task InstallQueueAsync()
    {
        if (SelectedAssetLibrary is null)
            return;

        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            var installQueue = QueueArchives
                .Where(x => x.ArchiveStatus is ArchiveStatus.Ready)
                .ToList();

            if (installQueue.Count == 0)
            {
                StatusText = "Nothing to install";
                return;
            }

            var promotedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var progress in archiveInstaller.InstallArchivesAsync(SelectedAssetLibrary.Id, installQueue))
            {
                var (percent, status) = QueueProgressAggregator.ComputeInstallProgress(installQueue);
                StatusText = status;
                ProgressPercent = percent;

                if (progress.ArchiveStatus == ArchiveStatus.Installed
                    && promotedPaths.Add(progress.ArchivePath))
                {
                    await PromoteInstalledArchiveAsync(progress);
                }
            }

            InstallQueueCommand.NotifyCanExecuteChanged();

            await LoadLibrariesAsync();
            await RefreshSelectedAssetLibrarySummaryAsync();
            SortQueueArchives();
            StatusText = "Install queue finished";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveQueuedArchives))]
    private void RemoveQueuedArchives()
    {
        var toRemove = SelectedQueueArchives.ToList();
        if (toRemove.Count == 0)
            return;

        foreach (var archive in toRemove)
            QueueArchives.Remove(archive);

        SelectedQueueArchives.Clear();
        InstallQueueCommand.NotifyCanExecuteChanged();
        SortQueueArchives();
        StatusText = QueueArchives.Count == 0 ? "Queue cleared" : $"{QueueArchives.Count} archive(s) queued";
    }

    [RelayCommand]
    private void ClearQueue()
    {
        QueueArchives.Clear();
        InstallQueueCommand.NotifyCanExecuteChanged();
        StatusText = "Queue cleared";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadLibrariesAsync();
        await RefreshInstalledArchivesAsync();
        StatusText = "Refreshed";
    }

    [RelayCommand(CanExecute = nameof(CanUninstallSelectedArchive))]
    private async Task UninstallSelectedArchiveAsync()
    {
        var toUninstall = SelectedInstalledArchives
            .Where(x => x.Status == ArchiveStatus.Installed)
            .ToList();
        if (toUninstall.Count == 0)
            return;

        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            foreach (var archive in toUninstall)
            {
                StatusText = $"Uninstalling {archive.ArchiveName}";
                await archiveInstaller.UninstallArchiveAsync(archive.Id);
            }

            await RefreshInstalledArchivesAsync();
            await LoadSelectedArchiveDetailsAsync(SelectedInstalledArchive);

            StatusText = toUninstall.Count == 1
                ? "Archive uninstalled"
                : $"{toUninstall.Count} archives uninstalled";
            ProgressPercent = 100;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReinstallSelectedArchive))]
    private async Task ReinstallSelectedArchiveAsync()
    {
        var toReinstall = SelectedInstalledArchives
            .Where(x => x.Status == ArchiveStatus.Uninstalled && File.Exists(x.BackupPath))
            .ToList();
        if (toReinstall.Count == 0)
            return;

        IsBusy = true;
        ProgressPercent = 0;
        SelectedInstalledArchives.Clear();

        var queueItems = new List<LoadedArchive>();
        foreach (var archive in toReinstall)
        {
            var existing = InstalledArchives.FirstOrDefault(x => x.Id == archive.Id);
            if (existing is not null)
                InstalledArchives.Remove(existing);

            var queueItem = CreateLoadedArchiveForReinstall(archive);
            QueueArchives.Add(queueItem);
            queueItems.Add(queueItem);
        }

        ApplyInstalledArchiveFilter();
        SelectedInstalledArchive = InstalledArchives.FirstOrDefault();
        SortQueueArchives();
        InstallQueueCommand.NotifyCanExecuteChanged();
        ReinstallSelectedArchiveCommand.NotifyCanExecuteChanged();

        try
        {
            var promotedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var archive in toReinstall)
            {
                var queueItem = queueItems.First(x => x.ArchiveId == archive.Id);

                await foreach (var progress in archiveInstaller.ReinstallArchiveAsync(archive.Id, queueItem))
                {
                    var (percent, status) = QueueProgressAggregator.ComputeInstallProgress(queueItems);
                    StatusText = status;
                    ProgressPercent = percent;

                    if (progress.ArchiveStatus is ArchiveStatus.Installed or ArchiveStatus.Error
                        && promotedPaths.Add(progress.ArchivePath))
                    {
                        await PromoteInstalledArchiveAsync(progress);
                    }
                }
            }

            await LoadSelectedArchiveDetailsAsync(SelectedInstalledArchive);
            SortQueueArchives();
            StatusText = queueItems.All(x => x.ArchiveStatus == ArchiveStatus.Installed)
                ? queueItems.Count == 1
                    ? "Archive reinstalled"
                    : $"{queueItems.Count} archives reinstalled"
                : "Reinstall queue finished";
        }
        finally
        {
            IsBusy = false;
            ReinstallSelectedArchiveCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanForgetSelectedArchive))]
    private async Task ForgetSelectedArchiveAsync()
    {
        var toForget = SelectedInstalledArchives
            .Where(x => x.Status is ArchiveStatus.Uninstalled or ArchiveStatus.Error)
            .ToList();
        if (toForget.Count == 0)
            return;

        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            foreach (var archive in toForget)
            {
                StatusText = $"Removing database records for {archive.ArchiveName}";
                await archiveInstaller.ForgetArchiveAsync(archive.Id);
            }

            await RefreshInstalledArchivesAsync();
            await LoadSelectedArchiveDetailsAsync(SelectedInstalledArchive);

            StatusText = toForget.Count == 1
                ? "Archive removed from install manager"
                : $"{toForget.Count} archives removed from install manager";
            ProgressPercent = 100;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReloadAfterSettingsAsync()
    {
        await settingsService.EnsureSettingsLoadedAsync();
        await LoadLibrariesAsync();
        await RefreshInstalledArchivesAsync();
        StatusText = "Settings updated";
    }

    private bool CanInstallQueue()
    {
        return !IsBusy && SelectedAssetLibrary is not null &&
               QueueArchives.Any(x => x.ArchiveStatus == ArchiveStatus.Ready);
    }

    private bool CanRemoveQueuedArchives()
    {
        return !IsBusy && SelectedQueueArchives.Count > 0;
    }

    private bool CanUninstallSelectedArchive()
    {
        return !IsBusy && SelectedInstalledArchives.Any(x => x.Status == ArchiveStatus.Installed);
    }

    private bool CanReinstallSelectedArchive()
    {
        return !IsBusy && SelectedInstalledArchives.Any(x =>
            x.Status == ArchiveStatus.Uninstalled && File.Exists(x.BackupPath));
    }

    private bool CanForgetSelectedArchive()
    {
        return !IsBusy &&
               SelectedInstalledArchives.Any(x => x.Status is ArchiveStatus.Uninstalled or ArchiveStatus.Error);
    }

    private async Task LoadLibrariesAsync()
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var libraries = await dbContext.AssetLibraries.OrderBy(x => x.Name).ToListAsync();
        AssetLibraries.Clear();

        foreach (var library in libraries)
        {
            AssetLibraries.Add(new AssetLibraryItemViewModel
            {
                Id = library.Id,
                Name = library.Name,
                Path = library.Path,
                ArchiveBackupPath = library.ArchiveBackupPath,
                IsDefault = library.Id == settingsService.CurrentSettings.DefaultAssetLibrary
            });
        }

        AssetLibraries.SortBy(x => x.Name);
        SelectedAssetLibrary = AssetLibraries.FirstOrDefault(x => x.IsDefault) ?? AssetLibraries.FirstOrDefault();
    }

    private async Task RefreshInstalledArchivesAsync()
    {
        var selectedIds = SelectedInstalledArchives.Select(x => x.Id).ToHashSet();
        InstalledArchives.Clear();
        SelectedInstalledArchives.Clear();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var topLevelArchives = await dbContext.Archives
            .Include(x => x.AssetLibrary)
            .Include(x => x.AssetFiles)
            .ThenInclude(x => x.InstallRecord)
            .Where(x => x.ParentArchiveId == null &&
                        (SelectedAssetLibrary == null || x.AssetLibraryId == SelectedAssetLibrary.Id))
            .ToListAsync();

        foreach (var archive in topLevelArchives.OrderBy(x => x.ArchiveName, StringComparer.OrdinalIgnoreCase))
        {
            var viewModel = await CreateInstalledArchiveViewModelAsync(dbContext, archive);
            InstalledArchives.Add(viewModel);
        }

        foreach (var archive in InstalledArchives.Where(x => selectedIds.Contains(x.Id)))
            SelectedInstalledArchives.Add(archive);

        SelectedInstalledArchive = SelectedInstalledArchives.FirstOrDefault()
                                   ?? InstalledArchives.FirstOrDefault();

        ApplyInstalledArchiveFilter();
        await RefreshSelectedAssetLibrarySummaryAsync();
    }

    private async Task PromoteInstalledArchiveAsync(LoadedArchive loadedArchive)
    {
        if (loadedArchive.ArchiveId is null)
            return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var archive = await dbContext.Archives
            .Include(x => x.AssetLibrary)
            .Include(x => x.AssetFiles)
            .ThenInclude(x => x.InstallRecord)
            .FirstOrDefaultAsync(x => x.Id == loadedArchive.ArchiveId && x.ParentArchiveId == null);

        if (archive is null)
            return;

        var viewModel = await CreateInstalledArchiveViewModelAsync(dbContext, archive);

        await UiThread.RunAsync(() =>
        {
            var queued = QueueArchives.FirstOrDefault(x =>
                string.Equals(x.ArchivePath, loadedArchive.ArchivePath, StringComparison.OrdinalIgnoreCase));
            if (queued is not null)
                QueueArchives.Remove(queued);

            var existingIndex = -1;
            for (var index = 0; index < InstalledArchives.Count; index++)
            {
                if (InstalledArchives[index].Id != viewModel.Id)
                    continue;

                existingIndex = index;
                break;
            }

            if (existingIndex >= 0)
                InstalledArchives[existingIndex] = viewModel;
            else
                InstalledArchives.Add(viewModel);

            InstalledArchives.SortBy(x => x.ArchiveName);
            ApplyInstalledArchiveFilter();
            InstallQueueCommand.NotifyCanExecuteChanged();
        });

        await RefreshSelectedAssetLibrarySummaryAsync();
    }

    private async Task<InstalledArchiveViewModel> CreateInstalledArchiveViewModelAsync(ApplicationDbContext dbContext,
        Archive archive)
    {
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, archive.Id);
        var records = await dbContext.InstallRecords
            .Where(x => archiveIds.Contains(x.ArchiveId))
            .ToListAsync();

        return new InstalledArchiveViewModel
        {
            Id = archive.Id,
            ArchiveName = archive.ArchiveName,
            AssetLibraryName = archive.AssetLibrary.Name,
            ContentRoot = archive.ContentRoot,
            BackupPath = GetDerivedBackupPath(archive.AssetLibrary, archive.ArchiveName),
            Status = archive.Status,
            AssetTypes = AssetTypeCollection.Parse(archive.AssetTypes),
            Categories = ParseCategories(archive.Categories),
            InstalledAt = archive.InstallCompletedAt ?? archive.InstallStartedAt,
            ArchiveSize = archive.ArchiveSize,
            FileCount = records.Count,
            ActiveFileCount = records.Count(x =>
                !x.HasBeenOverriden && x.InstallRecordStatus != InstallRecordStatus.Uninstalled),
            ReplacedFileCount = records.Count(x => x.InstallRecordStatus == InstallRecordStatus.Replaced),
            SkippedFileCount = records.Count(x => x.InstallRecordStatus == InstallRecordStatus.Skipped),
            ErrorMessage = archive.ErrorMessage
        };
    }

    private async Task RefreshSelectedAssetLibrarySummaryAsync()
    {
        if (SelectedAssetLibrary is null)
        {
            SelectedAssetLibraryArchiveCount = 0;
            SelectedAssetLibraryInstalledArchiveCount = 0;
            SelectedAssetLibraryFileCount = 0;
            SelectedAssetLibraryActiveFileCount = 0;
            SelectedAssetLibraryTotalArchiveSize = 0;
            SelectedAssetLibraryTotalContentSize = 0;
            return;
        }

        var statistics = await assetLibraryStatisticsService.GetStatisticsAsync(SelectedAssetLibrary.Id);
        SelectedAssetLibraryArchiveCount = statistics.ArchiveCount;
        SelectedAssetLibraryInstalledArchiveCount = statistics.InstalledArchiveCount;
        SelectedAssetLibraryFileCount = statistics.FileCount;
        SelectedAssetLibraryActiveFileCount = statistics.ActiveFileCount;
        SelectedAssetLibraryTotalArchiveSize = statistics.TotalArchiveSize;
        SelectedAssetLibraryTotalContentSize = statistics.TotalContentSize;
    }

    private async Task LoadSelectedArchiveDetailsAsync(InstalledArchiveViewModel? archive)
    {
        SelectedArchiveFiles.Clear();
        FilteredSelectedArchiveFiles.Clear();
        OnPropertyChanged(nameof(ArchiveFileListSummary));
        if (archive is null)
            return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, archive.Id);
        var records = await dbContext.InstallRecords
            .Include(x => x.AssetFile)
            .Where(x => archiveIds.Contains(x.ArchiveId))
            .ToListAsync();

        foreach (var record in records
                     .OrderBy(x => x.AssetFile.InstalledRelativePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.InstalledAt))
        {
            SelectedArchiveFiles.Add(new ArchiveFileRecordViewModel
            {
                ArchiveRelativePath = record.AssetFile.ArchiveRelativePath,
                InstalledRelativePath = record.AssetFile.InstalledRelativePath,
                FileHash = record.AssetFile.FileHash,
                FileSize = record.AssetFile.FileSize,
                Status = record.InstallRecordStatus,
                IsOverridden = record.HasBeenOverriden,
                InstalledAt = record.InstalledAt
            });
        }

        ApplyArchiveFileFilter();
        OnPropertyChanged(nameof(ArchiveFileListSummary));
    }

    private void ApplyInstalledArchiveFilter()
    {
        FilteredInstalledArchives.Clear();

        var query = InstalledArchiveSearchText.Trim();
        var matches = (string.IsNullOrWhiteSpace(query)
                ? InstalledArchives
                : InstalledArchives.Where(x => MatchesInstalledArchiveSearch(x, query)))
            .OrderBy(x => x.ArchiveName, StringComparer.OrdinalIgnoreCase);

        foreach (var archive in matches)
            FilteredInstalledArchives.Add(archive);
    }

    private void ApplyArchiveFileFilter()
    {
        FilteredSelectedArchiveFiles.Clear();

        var query = ArchiveFileSearchText.Trim();
        var matches = (string.IsNullOrWhiteSpace(query)
                ? SelectedArchiveFiles
                : SelectedArchiveFiles.Where(x => MatchesArchiveFileSearch(x, query)))
            .OrderBy(x => x.InstalledRelativePath, StringComparer.OrdinalIgnoreCase);

        foreach (var file in matches)
            FilteredSelectedArchiveFiles.Add(file);
    }

    private static bool MatchesInstalledArchiveSearch(InstalledArchiveViewModel archive, string query)
    {
        return Contains(archive.ArchiveName, query)
               || Contains(archive.AssetLibraryName, query)
               || Contains(archive.BackupPath, query)
               || Contains(archive.ContentRoot, query)
               || Contains(archive.Status.ToString(), query)
               || archive.Categories.Any(category => Contains(category, query))
               || archive.AssetTypes.Any(assetType => Contains(assetType.ToString(), query));
    }

    private static bool MatchesArchiveFileSearch(ArchiveFileRecordViewModel file, string query)
    {
        return Contains(file.InstalledRelativePath, query)
               || Contains(file.ArchiveRelativePath, query)
               || Contains(file.Status.ToString(), query);
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrEmpty(value)
               && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<Guid>> GetArchiveTreeIdsAsync(ApplicationDbContext dbContext, Guid rootId)
    {
        var queue = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
                continue;

            var children = await dbContext.Archives.Where(x => x.ParentArchiveId == current).Select(x => x.Id)
                .ToListAsync();
            foreach (var child in children)
                queue.Enqueue(child);
        }

        return visited;
    }

    private string GetDerivedBackupPath(AssetLibrary assetLibrary, string archiveName)
    {
        var backupRoot = assetLibrary.ArchiveBackupPath;
        if (string.IsNullOrWhiteSpace(backupRoot))
            backupRoot = settingsService.CurrentSettings.DefaultArchiveBackupPath;
        if (string.IsNullOrWhiteSpace(backupRoot))
            backupRoot = installerConfig.ArchiveBackupPath;

        return Path.Combine(backupRoot, archiveName);
    }

    private static LoadedArchive CreateLoadedArchiveForReinstall(InstalledArchiveViewModel archive)
    {
        var loaded = new LoadedArchive(archive.BackupPath)
        {
            ArchiveId = archive.Id,
            ArchiveStatus = ArchiveStatus.Ready,
            StatusText = "Ready to reinstall",
            ContentRoot = archive.ContentRoot,
            FileSizeBytes = archive.ArchiveSize
        };

        foreach (var assetType in archive.AssetTypes)
            loaded.AssetTypes.Add(assetType);

        return loaded;
    }

    private static List<string> ParseCategories(string categories)
    {
        return categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SortQueueArchives(ObservableCollection<LoadedArchive> queueArchives)
    {
        queueArchives.SortBy(x => x.DisplayName);
    }

    private void SortQueueArchives() => SortQueueArchives(QueueArchives);
}