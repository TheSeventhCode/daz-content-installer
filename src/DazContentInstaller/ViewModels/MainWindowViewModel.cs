using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
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
    IAssetLibraryStatisticsService assetLibraryStatisticsService,
    IInstallRecordStatisticsService installRecordStatisticsService,
    IArchiveFileDetailsService archiveFileDetailsService,
    IArchiveOverrideService archiveOverrideService,
    IInterruptedInstallRecoveryService interruptedInstallRecoveryService)
    : ViewModelBase
{
    private const int CollectionUpdateBatchSize = 50;
    private static readonly TimeSpan UiProgressUpdateInterval = TimeSpan.FromMilliseconds(100);
    private static readonly InstalledArchiveSortOptionViewModel NameAscendingInstalledArchiveSortOption =
        new(InstalledArchiveSortMode.NameAscending, "Name (A-Z)");
    private static readonly InstalledArchiveSortOptionViewModel NameDescendingInstalledArchiveSortOption =
        new(InstalledArchiveSortMode.NameDescending, "Name (Z-A)");
    private static readonly InstalledArchiveSortOptionViewModel InstalledNewestArchiveSortOption =
        new(InstalledArchiveSortMode.InstalledNewest, "Installed newest");
    private static readonly InstalledArchiveSortOptionViewModel InstalledOldestArchiveSortOption =
        new(InstalledArchiveSortMode.InstalledOldest, "Installed oldest");

    public const string AllCategoriesLabel = "All categories";

    public ObservableCollection<LoadedArchive> QueueArchives { get; } = [];
    public ObservableCollection<LoadedArchive> FilteredQueueArchives { get; } = [];
    private ObservableCollection<InstalledArchiveViewModel> InstalledArchives { get; } = [];
    public ObservableCollection<InstalledArchiveViewModel> FilteredInstalledArchives { get; } = [];
    public ObservableCollection<string> AvailableCategoryFilters { get; } = [AllCategoriesLabel];
    public ObservableCollection<InstalledArchiveSortOptionViewModel> AvailableInstalledArchiveSortOptions { get; } =
    [
        NameAscendingInstalledArchiveSortOption,
        NameDescendingInstalledArchiveSortOption,
        InstalledNewestArchiveSortOption,
        InstalledOldestArchiveSortOption
    ];
    public ObservableCollection<InstalledArchiveViewModel> SelectedInstalledArchives { get; } = [];
    public ObservableCollection<LoadedArchive> SelectedQueueArchives { get; } = [];
    public ObservableCollection<AssetLibraryItemViewModel> AssetLibraries { get; } = [];
    private ObservableCollection<ArchiveFileGroupViewModel> SelectedArchiveFileGroups { get; } = [];
    public ObservableCollection<ArchiveFileGroupViewModel> FilteredSelectedArchiveFileGroups { get; } = [];

    private bool _selectionHandlersRegistered;
    private bool _suppressLibraryRefresh;
    private readonly ProgressUpdateThrottler _uiProgressThrottler = new(UiProgressUpdateInterval);
    private CancellationTokenSource? _archiveDetailsLoadCts;
    private CancellationTokenSource? _installedArchivesRefreshCts;
    private int _activeScanOperations;
    private int _activeInstallOperations;
    private int _activeMaintenanceOperations;
    private int _scanProgressCompleted;
    private int _scanProgressTotal;

    public bool IsScanningArchives => _activeScanOperations > 0;

    public bool IsInstallingArchives => _activeInstallOperations > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallQueueCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveQueuedArchivesCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial bool IsBlocked { get; set; }

    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty] public partial double ProgressPercent { get; set; }

    [ObservableProperty] public partial bool IsLoadingInstalledArchives { get; set; }

    public string WindowTitle => IsBusy ? "DazContentInstaller — Working…" : "DazContentInstaller";

    public bool ShowIndeterminateProgress => IsBusy && ProgressPercent <= 0;

    public bool CanOpenOverrides =>
        SelectedInstalledArchive?.Status == ArchiveStatus.Installed && !IsInstallingArchives;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(ShowIndeterminateProgress));
        OnPropertyChanged(nameof(CanOpenOverrides));
    }

    private void BeginScanOperation(int total)
    {
        if (Interlocked.Increment(ref _activeScanOperations) == 1)
        {
            _scanProgressCompleted = 0;
            _scanProgressTotal = total;
        }

        UpdateBusyState();
        OnPropertyChanged(nameof(IsScanningArchives));
        InstallQueueCommand.NotifyCanExecuteChanged();
        RemoveQueuedArchivesCommand.NotifyCanExecuteChanged();
    }

    private void EndScanOperation()
    {
        Interlocked.Decrement(ref _activeScanOperations);
        UpdateBusyState();
        OnPropertyChanged(nameof(IsScanningArchives));
        InstallQueueCommand.NotifyCanExecuteChanged();
        RemoveQueuedArchivesCommand.NotifyCanExecuteChanged();
    }

    private void BeginInstallOperation()
    {
        Interlocked.Increment(ref _activeInstallOperations);
        UpdateBusyState();
        OnPropertyChanged(nameof(IsInstallingArchives));
        InstallQueueCommand.NotifyCanExecuteChanged();
        UninstallSelectedArchiveCommand.NotifyCanExecuteChanged();
        ReinstallSelectedArchiveCommand.NotifyCanExecuteChanged();
        ForgetSelectedArchiveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenOverrides));
    }

    private void EndInstallOperation()
    {
        Interlocked.Decrement(ref _activeInstallOperations);
        UpdateBusyState();
        OnPropertyChanged(nameof(IsInstallingArchives));
        InstallQueueCommand.NotifyCanExecuteChanged();
        UninstallSelectedArchiveCommand.NotifyCanExecuteChanged();
        ReinstallSelectedArchiveCommand.NotifyCanExecuteChanged();
        ForgetSelectedArchiveCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenOverrides));
    }

    private void BeginMaintenanceOperation()
    {
        Interlocked.Increment(ref _activeMaintenanceOperations);
        UpdateBusyState();
    }

    private void EndMaintenanceOperation()
    {
        Interlocked.Decrement(ref _activeMaintenanceOperations);
        UpdateBusyState();
    }

    private void UpdateBusyState()
    {
        IsBusy = _activeScanOperations > 0 || _activeInstallOperations > 0 || _activeMaintenanceOperations > 0;
    }

    private void UpdateScanProgressUi(int completed, int total, bool force = false)
    {
        if (IsInstallingArchives)
            return;

        var (percent, status) = QueueProgressAggregator.ComputeScanProgress(completed, total);
        if (!_uiProgressThrottler.ShouldUpdate(force || completed == total))
            return;

        ProgressPercent = percent;
        StatusText = status;
    }

    private void UpdateCombinedOperationStatus()
    {
        if (IsInstallingArchives)
            return;

        if (IsScanningArchives)
            UpdateScanProgressUi(_scanProgressCompleted, _scanProgressTotal, force: true);
        else if (!IsBusy)
        {
            ProgressPercent = 0;
            StatusText = QueueArchives.Count == 0 ? "Ready" : $"{QueueArchives.Count} archive(s) queued";
        }
    }

    partial void OnProgressPercentChanged(double value) => OnPropertyChanged(nameof(ShowIndeterminateProgress));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallQueueCommand))]
    public partial AssetLibraryItemViewModel? SelectedAssetLibrary { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReinstallSelectedArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetSelectedArchiveCommand))]
    [NotifyPropertyChangedFor(nameof(CanOpenOverrides))]
    public partial InstalledArchiveViewModel? SelectedInstalledArchive { get; set; }

    [ObservableProperty] public partial LoadedArchive? SelectedQueueArchive { get; set; }

    [ObservableProperty] public partial string InstalledArchiveSearchText { get; set; } = string.Empty;

    [ObservableProperty] public partial string SelectedCategoryFilter { get; set; } = AllCategoriesLabel;

    [ObservableProperty]
    public partial InstalledArchiveSortOptionViewModel SelectedInstalledArchiveSortOption { get; set; } =
        NameAscendingInstalledArchiveSortOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllInstalledArchiveStatusFilterSelected))]
    [NotifyPropertyChangedFor(nameof(IsUninstalledInstalledArchiveStatusFilterSelected))]
    [NotifyPropertyChangedFor(nameof(IsFailedInstalledArchiveStatusFilterSelected))]
    public partial InstalledArchiveStatusFilter SelectedInstalledArchiveStatusFilter { get; set; } =
        InstalledArchiveStatusFilter.All;

    public bool IsAllInstalledArchiveStatusFilterSelected =>
        SelectedInstalledArchiveStatusFilter == InstalledArchiveStatusFilter.All;

    public bool IsUninstalledInstalledArchiveStatusFilterSelected =>
        SelectedInstalledArchiveStatusFilter == InstalledArchiveStatusFilter.Uninstalled;

    public bool IsFailedInstalledArchiveStatusFilterSelected =>
        SelectedInstalledArchiveStatusFilter == InstalledArchiveStatusFilter.Failed;

    [ObservableProperty] public partial string QueueArchiveSearchText { get; set; } = string.Empty;

    [ObservableProperty] public partial string ArchiveFileSearchText { get; set; } = string.Empty;

    [ObservableProperty] private partial int SelectedAssetLibraryArchiveCount { get; set; }

    [ObservableProperty] private partial int SelectedAssetLibraryInstalledArchiveCount { get; set; }

    [ObservableProperty] private partial int SelectedAssetLibraryFileCount { get; set; }

    [ObservableProperty] private partial int SelectedAssetLibraryActiveFileCount { get; set; }

    [ObservableProperty] private partial ulong SelectedAssetLibraryTotalArchiveSize { get; set; }

    [ObservableProperty] private partial ulong SelectedAssetLibraryTotalContentSize { get; set; }

    public string ArchiveFileListSummary
    {
        get
        {
            var total = SelectedArchiveFileGroups.Sum(x => x.Files.Count);
            if (string.IsNullOrWhiteSpace(ArchiveFileSearchText))
                return $"{total} entries";

            var filtered = FilteredSelectedArchiveFileGroups.Sum(x => x.FilteredFiles.Count);
            return $"{filtered} of {total} entries";
        }
    }

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
        await interruptedInstallRecoveryService.RecoverInterruptedInstallsAsync();
        await LoadLibrariesAsync();
        QueueInstalledArchivesRefresh();
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

    partial void OnSelectedCategoryFilterChanged(string value) => ApplyInstalledArchiveFilter();

    partial void OnSelectedInstalledArchiveSortOptionChanged(InstalledArchiveSortOptionViewModel value) =>
        ApplyInstalledArchiveFilter();

    partial void OnSelectedInstalledArchiveStatusFilterChanged(InstalledArchiveStatusFilter value) =>
        ApplyInstalledArchiveFilter();

    partial void OnQueueArchiveSearchTextChanged(string value) => ApplyQueueArchiveFilter();

    partial void OnArchiveFileSearchTextChanged(string value)
    {
        ApplyArchiveFileFilter();
        OnPropertyChanged(nameof(ArchiveFileListSummary));
    }

    partial void OnSelectedAssetLibraryChanged(AssetLibraryItemViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedAssetLibrarySummary));
        if (_suppressLibraryRefresh)
            return;

        QueueInstalledArchivesRefresh();
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
        IsBlocked = false;
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

        BeginScanOperation(addedArchives.Count);
        ProgressPercent = 0;
        _uiProgressThrottler.ShouldUpdate(force: true);
        var archivesToScan = LoadedArchive.OrderByDisplayName(addedArchives);
        var total = archivesToScan.Count;
        var completedScans = 0;
        var scanLock = new object();
        var maxScanParallelism = Math.Max(1, installerConfig.MaxConcurrentScans);
        var workChannel = Channel.CreateUnbounded<LoadedArchive>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

        foreach (var archive in archivesToScan)
            await workChannel.Writer.WriteAsync(archive);

        workChannel.Writer.Complete();

        try
        {
            var workerCount = Math.Min(maxScanParallelism, archivesToScan.Count);
            var scanWorkers = Enumerable.Range(0, workerCount)
                .Select(_ => RunScanArchiveWorkerAsync(workChannel.Reader, total, () =>
                {
                    lock (scanLock)
                    {
                        completedScans++;
                        return completedScans;
                    }
                }))
                .ToArray();

            await Task.WhenAll(scanWorkers);
        }
        finally
        {
            EndScanOperation();
            InstallQueueCommand.NotifyCanExecuteChanged();
            UpdateCombinedOperationStatus();
            SortQueueArchives();
        }
    }

    [RelayCommand]
    private void SelectInstalledArchiveStatusFilter(InstalledArchiveStatusFilter filter)
    {
        SelectedInstalledArchiveStatusFilter = filter;
    }

    [RelayCommand(CanExecute = nameof(CanInstallQueue))]
    private async Task InstallQueueAsync()
    {
        IsBlocked = false;
        if (SelectedAssetLibrary is null)
            return;

        BeginInstallOperation();
        ProgressPercent = 0;
        _uiProgressThrottler.ShouldUpdate(force: true);
        try
        {
            var installQueue = LoadedArchive.OrderByDisplayName(
                QueueArchives.Where(x => x.ArchiveStatus is ArchiveStatus.Ready));

            if (installQueue.Count == 0)
            {
                StatusText = "Nothing to install";
                return;
            }

            await foreach (var progress in archiveInstaller.InstallArchivesAsync(SelectedAssetLibrary.Id, installQueue))
            {
                UpdateInstallProgressUi(installQueue, progress);

                if (progress.InstallCompleted)
                    await PromoteInstalledArchiveAsync(progress);
            }

            UpdateInstallProgressUi(installQueue, force: true);
            await RefreshInstalledArchivesAsync();
            InstallQueueCommand.NotifyCanExecuteChanged();
            SortQueueArchives();
        }
        finally
        {
            EndInstallOperation();
            UpdateCombinedOperationStatus();
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
        ApplyQueueArchiveFilter();
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
        IsBlocked = false;
        var toUninstall = SelectedInstalledArchives
            .Where(IsUninstallEligible)
            .ToList();
        if (toUninstall.Count == 0)
            return;

        BeginMaintenanceOperation();
        ProgressPercent = 0;
        _uiProgressThrottler.ShouldUpdate(force: true);
        try
        {
            var total = toUninstall.Count;
            var blockedMessages = new List<string>();
            for (var index = 0; index < toUninstall.Count; index++)
            {
                var archive = toUninstall[index];
                if (_uiProgressThrottler.ShouldUpdate(force: index == total - 1))
                    StatusText = $"Uninstalling {archive.EffectiveDisplayName} ({index + 1}/{total})";

                try
                {
                    await Task.Run(() => archiveInstaller.UninstallArchiveAsync(archive.Id));
                }
                catch (InvalidOperationException ex)
                {
                    blockedMessages.Add($"{archive.EffectiveDisplayName}: {ex.Message}");
                }

                await Task.Yield();
            }

            await RefreshInstalledArchivesAsync();
            await LoadSelectedArchiveDetailsAsync(SelectedInstalledArchive);

            if (blockedMessages.Count > 0)
            {
                StatusText = blockedMessages.Count == 1
                    ? blockedMessages[0]
                    : string.Join(" · ", blockedMessages);
                ProgressPercent = 100;
                IsBlocked = true;
                return;
            }

            StatusText = toUninstall.Count == 1
                ? toUninstall[0].Status == ArchiveStatus.Installed
                    ? "Archive uninstalled"
                    : "Failed install cleaned up"
                : $"{toUninstall.Count} archives cleaned up";
            ProgressPercent = 100;
        }
        finally
        {
            EndMaintenanceOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanReinstallSelectedArchive))]
    private async Task ReinstallSelectedArchiveAsync()
    {
        IsBlocked = false;
        var toReinstall = SelectedInstalledArchives
            .Where(x => x.Status == ArchiveStatus.Uninstalled && File.Exists(x.BackupPath))
            .ToList();
        if (toReinstall.Count == 0)
            return;

        BeginMaintenanceOperation();
        ProgressPercent = 0;
        _uiProgressThrottler.ShouldUpdate(force: true);
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
            foreach (var queueItem in LoadedArchive.OrderByDisplayName(queueItems))
            {
                await foreach (var progress in archiveInstaller.ReinstallArchiveAsync(queueItem.ArchiveId!.Value,
                                   queueItem))
                {
                    UpdateInstallProgressUi(queueItems, progress);

                    if (progress.InstallCompleted)
                        await PromoteInstalledArchiveAsync(progress);
                }
            }

            UpdateInstallProgressUi(queueItems, force: true);
            await RefreshInstalledArchivesAsync();
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
            EndMaintenanceOperation();
            ReinstallSelectedArchiveCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanForgetSelectedArchive))]
    private async Task ForgetSelectedArchiveAsync()
    {
        IsBlocked = false;
        var toForget = SelectedInstalledArchives
            .Where(x => x.Status is ArchiveStatus.Uninstalled or ArchiveStatus.Error)
            .ToList();
        if (toForget.Count == 0)
            return;

        BeginMaintenanceOperation();
        ProgressPercent = 0;
        try
        {
            foreach (var archive in toForget)
            {
                StatusText = $"Removing database records for {archive.EffectiveDisplayName}";
                await Task.Run(() => archiveInstaller.ForgetArchiveAsync(archive.Id));
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
            EndMaintenanceOperation();
        }
    }

    public async Task ReloadAfterSettingsAsync()
    {
        await settingsService.EnsureSettingsLoadedAsync();
        await LoadLibrariesAsync();
        await RefreshInstalledArchivesAsync();
        StatusText = "Settings updated";
    }

    public async Task ReloadAfterOverridesAsync()
    {
        await RefreshInstalledArchivesAsync();
        await LoadSelectedArchiveDetailsAsync(SelectedInstalledArchive);
        StatusText = "Overrides updated";
    }

    private bool CanInstallQueue()
    {
        return !IsInstallingArchives && SelectedAssetLibrary is not null &&
               QueueArchives.Any(x => x.ArchiveStatus == ArchiveStatus.Ready);
    }

    private bool CanRemoveQueuedArchives()
    {
        return !IsInstallingArchives && SelectedQueueArchives.Count > 0;
    }

    private bool CanUninstallSelectedArchive()
    {
        return !IsInstallingArchives && SelectedInstalledArchives.Any(IsUninstallEligible);
    }

    private static bool IsUninstallEligible(InstalledArchiveViewModel archive)
    {
        return archive.Status is ArchiveStatus.Installed
            or ArchiveStatus.Error
            or ArchiveStatus.Loading
            or ArchiveStatus.Installing;
    }

    private bool CanReinstallSelectedArchive()
    {
        return !IsInstallingArchives && SelectedInstalledArchives.Any(x =>
            x.Status == ArchiveStatus.Uninstalled && File.Exists(x.BackupPath));
    }

    private bool CanForgetSelectedArchive()
    {
        return !IsInstallingArchives &&
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
                ArchiveThumbnailPath = library.ArchiveThumbnailPath,
                IsDefault = library.Id == settingsService.CurrentSettings.DefaultAssetLibrary
            });
        }

        AssetLibraries.SortBy(x => x.Name);
        _suppressLibraryRefresh = true;
        try
        {
            SelectedAssetLibrary = AssetLibraries.FirstOrDefault(x => x.IsDefault) ?? AssetLibraries.FirstOrDefault();
        }
        finally
        {
            _suppressLibraryRefresh = false;
        }
    }

    private sealed record InstalledArchivesRefreshPayload(
        List<InstalledArchiveViewModel> ViewModels,
        AssetLibraryStatistics Statistics);

    private async Task RefreshInstalledArchivesAsync()
    {
        _installedArchivesRefreshCts?.Cancel();
        _installedArchivesRefreshCts?.Dispose();
        var loadCts = new CancellationTokenSource();
        _installedArchivesRefreshCts = loadCts;
        var cancellationToken = loadCts.Token;

        var selectedLibraryId = SelectedAssetLibrary?.Id;
        var selectedIds = SelectedInstalledArchives.Select(x => x.Id).ToHashSet();

        await UiThread.RunAsync(() => IsLoadingInstalledArchives = true);

        try
        {
            var payload = await Task.Run(
                    () => BuildInstalledArchivesRefreshPayloadAsync(selectedLibraryId, cancellationToken),
                    cancellationToken);

            await UiThread.RunAsync(async () =>
            {
                if (_installedArchivesRefreshCts != loadCts || SelectedAssetLibrary?.Id != selectedLibraryId)
                    return;

                InstalledArchives.Clear();
                SelectedInstalledArchives.Clear();

                await AddToCollectionInBatchesAsync(InstalledArchives, payload.ViewModels, cancellationToken);

                await AddToCollectionInBatchesAsync(
                    SelectedInstalledArchives,
                    InstalledArchives.Where(x => selectedIds.Contains(x.Id)),
                    cancellationToken);

                SelectedInstalledArchive = SelectedInstalledArchives.FirstOrDefault()
                                           ?? InstalledArchives.FirstOrDefault();

                ApplyAssetLibraryStatistics(payload.Statistics);
                RefreshAvailableCategoryFilters();
                await ApplyInstalledArchiveFilterAsync(cancellationToken);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await UiThread.RunAsync(() =>
            {
                if (_installedArchivesRefreshCts == loadCts)
                    IsLoadingInstalledArchives = false;
            });
        }
    }

    private void QueueInstalledArchivesRefresh()
    {
        _ = RefreshInstalledArchivesSafelyAsync();
    }

    private async Task RefreshInstalledArchivesSafelyAsync()
    {
        try
        {
            await RefreshInstalledArchivesAsync();
        }
        catch (Exception ex)
        {
            await UiThread.RunAsync(() =>
            {
                StatusText = $"Failed to load archives: {ex.Message}";
                ProgressPercent = 0;
            });
        }
    }

    private async Task<InstalledArchivesRefreshPayload> BuildInstalledArchivesRefreshPayloadAsync(
        Guid? selectedLibraryId, CancellationToken cancellationToken)
    {
        if (selectedLibraryId is null)
            return new InstalledArchivesRefreshPayload([], EmptyAssetLibraryStatistics);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var topLevelArchives = await dbContext.Archives
            .AsNoTracking()
            .Include(x => x.AssetLibrary)
            .Where(x => x.ParentArchiveId == null &&
                        x.Status != ArchiveStatus.Loading &&
                        x.Status != ArchiveStatus.Installing &&
                        x.Status != ArchiveStatus.Ready &&
                        x.Status != ArchiveStatus.Pending &&
                        x.AssetLibraryId == selectedLibraryId)
            .ToListAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var (countsByRootArchiveId, descendantSearchSegmentsByRootArchiveId) =
            await BuildInstalledArchiveTreeDataAsync(dbContext, topLevelArchives, cancellationToken);
        var rootArchiveIds = topLevelArchives.Select(x => x.Id).ToList();
        var overrideCountsByRootArchiveId = rootArchiveIds.Count == 0
            ? []
            : await dbContext.ArchiveOverrides
                .AsNoTracking()
                .Where(x => rootArchiveIds.Contains(x.RootArchiveId))
                .GroupBy(x => x.RootArchiveId)
                .Select(g => new { RootArchiveId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RootArchiveId, x => x.Count, cancellationToken);

        var viewModels = new List<InstalledArchiveViewModel>(topLevelArchives.Count);
        foreach (var archive in topLevelArchives.OrderBy(x =>
                         ArchiveDisplayName.GetEffectiveDisplayName(x.ArchiveName, x.DisplayName),
                     StringComparer.OrdinalIgnoreCase))
        {
            countsByRootArchiveId.TryGetValue(archive.Id, out var counts);
            counts ??= InstallRecordCounts.Empty;
            overrideCountsByRootArchiveId.TryGetValue(archive.Id, out var overrideCount);
            descendantSearchSegmentsByRootArchiveId.TryGetValue(archive.Id, out var descendantSearchSegments);
            viewModels.Add(CreateInstalledArchiveViewModel(
                archive, counts, overrideCount, descendantSearchSegments ?? []));
        }

        var statistics = await assetLibraryStatisticsService
            .GetStatisticsAsync(selectedLibraryId.Value, cancellationToken);

        return new InstalledArchivesRefreshPayload(viewModels, statistics);
    }

    private void UpdateInstallProgressUi(IReadOnlyList<LoadedArchive> installQueue, LoadedArchive? progress = null,
        bool force = false)
    {
        var isTerminal = progress?.ArchiveStatus is ArchiveStatus.Installed or ArchiveStatus.Error
            or ArchiveStatus.Duplicate;
        if (!_uiProgressThrottler.ShouldUpdate(force || isTerminal))
            return;

        progress?.RefreshQueueItemBindings();

        var (percent, status) = QueueProgressAggregator.ComputeInstallProgress(installQueue);
        StatusText = status;
        ProgressPercent = percent;
    }

    private async Task PromoteInstalledArchiveAsync(LoadedArchive loadedArchive)
    {
        if (loadedArchive.ArchiveId is null)
            return;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var archive = await dbContext.Archives
            .Include(x => x.AssetLibrary)
            .FirstOrDefaultAsync(x => x.Id == loadedArchive.ArchiveId && x.ParentArchiveId == null);

        if (archive is null || archive.Status != ArchiveStatus.Installed)
            return;

        var archiveIds = await GetArchiveTreeIdsForLibraryAsync(
            dbContext, archive.AssetLibraryId, archive.Id);
        var counts = await installRecordStatisticsService.GetCountsForArchivesAsync(dbContext, archiveIds);
        var overrideCount = await archiveOverrideService.GetOverrideCountAsync(archive.Id);
        var descendantSearchSegments = await LoadDescendantSearchSegmentsAsync(
            dbContext, archive.AssetLibraryId, archive.Id);
        var viewModel = CreateInstalledArchiveViewModel(archive, counts, overrideCount, descendantSearchSegments);

        await UiThread.RunAsync(() =>
        {
            loadedArchive.RefreshQueueItemBindings();

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

            RefreshAvailableCategoryFilters();
            ApplyInstalledArchiveFilter();
            ApplyQueueArchiveFilter();
            InstallQueueCommand.NotifyCanExecuteChanged();
        });

        await RefreshSelectedAssetLibrarySummaryAsync();
    }

    private async Task<(Dictionary<Guid, InstallRecordCounts> CountsByRoot,
            Dictionary<Guid, IReadOnlyList<InstalledArchiveSearchSegment>> DescendantSegmentsByRoot)>
        BuildInstalledArchiveTreeDataAsync(
            ApplicationDbContext dbContext, IReadOnlyList<Archive> topLevelArchives,
            CancellationToken cancellationToken = default)
    {
        if (topLevelArchives.Count == 0)
            return ([], []);

        var libraryIds = topLevelArchives.Select(x => x.AssetLibraryId).Distinct().ToList();
        var archivesInLibraries = await dbContext.Archives
            .AsNoTracking()
            .Where(x => libraryIds.Contains(x.AssetLibraryId))
            .ToListAsync(cancellationToken);

        var parentById = archivesInLibraries.ToDictionary(x => x.Id, x => x.ParentArchiveId);
        var archiveById = archivesInLibraries.ToDictionary(x => x.Id);

        var treeIdsByRoot = new Dictionary<Guid, HashSet<Guid>>();
        var allTreeArchiveIds = new HashSet<Guid>();
        foreach (var archive in topLevelArchives)
        {
            var treeIds = ArchiveTreeResolver.GetTreeIds(parentById, archive.Id);
            treeIdsByRoot[archive.Id] = treeIds;
            allTreeArchiveIds.UnionWith(treeIds);
        }

        var countsByArchiveId =
            await installRecordStatisticsService.GetCountsGroupedByArchiveIdAsync(dbContext, allTreeArchiveIds);

        var countsByRootArchiveId = new Dictionary<Guid, InstallRecordCounts>(topLevelArchives.Count);
        var descendantSegmentsByRootArchiveId =
            new Dictionary<Guid, IReadOnlyList<InstalledArchiveSearchSegment>>(topLevelArchives.Count);
        foreach (var (rootArchiveId, treeIds) in treeIdsByRoot)
        {
            countsByRootArchiveId[rootArchiveId] = ArchiveTreeResolver.RollUpCounts(treeIds, countsByArchiveId);
            descendantSegmentsByRootArchiveId[rootArchiveId] = treeIds
                .Where(id => id != rootArchiveId)
                .Select(id => InstalledArchiveSearchSegment.FromArchive(archiveById[id]))
                .ToList();
        }

        return (countsByRootArchiveId, descendantSegmentsByRootArchiveId);
    }

    private static async Task<IReadOnlyList<InstalledArchiveSearchSegment>> LoadDescendantSearchSegmentsAsync(
        ApplicationDbContext dbContext, Guid assetLibraryId, Guid rootArchiveId,
        CancellationToken cancellationToken = default)
    {
        var archivesInLibrary = await dbContext.Archives
            .AsNoTracking()
            .Where(x => x.AssetLibraryId == assetLibraryId)
            .ToListAsync(cancellationToken);

        var parentById = archivesInLibrary.ToDictionary(x => x.Id, x => x.ParentArchiveId);
        var archiveById = archivesInLibrary.ToDictionary(x => x.Id);
        var treeIds = ArchiveTreeResolver.GetTreeIds(parentById, rootArchiveId);

        return treeIds
            .Where(id => id != rootArchiveId)
            .Select(id => InstalledArchiveSearchSegment.FromArchive(archiveById[id]))
            .ToList();
    }

    private InstalledArchiveViewModel CreateInstalledArchiveViewModel(
        Archive archive,
        InstallRecordCounts counts,
        int overrideCount = 0,
        IReadOnlyList<InstalledArchiveSearchSegment>? descendantSearchSegments = null)
    {
        var thumbnailPath = GetDerivedThumbnailPath(archive.AssetLibrary, archive.Id, archive.ThumbnailFileExtension);

        return new InstalledArchiveViewModel
        {
            Id = archive.Id,
            ArchiveName = archive.ArchiveName,
            DisplayName = archive.DisplayName,
            AssetLibraryName = archive.AssetLibrary.Name,
            ContentRoot = archive.ContentRoot,
            BackupPath = GetDerivedBackupPath(archive.AssetLibrary, archive.ArchiveName),
            ThumbnailPath = thumbnailPath,
            Status = archive.Status,
            AssetTypes = AssetTypeCollection.Parse(archive.AssetTypes),
            Categories = ParseCategories(archive.Categories),
            InstalledAt = archive.InstallCompletedAt ?? archive.InstallStartedAt,
            ArchiveSize = archive.ArchiveSize,
            FileCount = counts.FileCount,
            ActiveFileCount = counts.ActiveFileCount,
            ReplacedFileCount = counts.ReplacedFileCount,
            SkippedFileCount = counts.SkippedFileCount,
            OverrideCount = overrideCount,
            ErrorMessage = archive.ErrorMessage,
            DescendantSearchSegments = descendantSearchSegments ?? []
        };
    }

    private static readonly AssetLibraryStatistics EmptyAssetLibraryStatistics = new(0, 0, 0, 0, 0, 0);

    private async Task RefreshSelectedAssetLibrarySummaryAsync()
    {
        if (SelectedAssetLibrary is null)
        {
            ApplyAssetLibraryStatistics(EmptyAssetLibraryStatistics);
            return;
        }

        var statistics = await assetLibraryStatisticsService
            .GetStatisticsAsync(SelectedAssetLibrary.Id);

        await UiThread.RunAsync(() => ApplyAssetLibraryStatistics(statistics));
    }

    private void ApplyAssetLibraryStatistics(AssetLibraryStatistics statistics)
    {
        SelectedAssetLibraryArchiveCount = statistics.ArchiveCount;
        SelectedAssetLibraryInstalledArchiveCount = statistics.InstalledArchiveCount;
        SelectedAssetLibraryFileCount = statistics.FileCount;
        SelectedAssetLibraryActiveFileCount = statistics.ActiveFileCount;
        SelectedAssetLibraryTotalArchiveSize = statistics.TotalArchiveSize;
        SelectedAssetLibraryTotalContentSize = statistics.TotalContentSize;
    }

    private async Task LoadSelectedArchiveDetailsAsync(InstalledArchiveViewModel? archive)
    {
        if (_archiveDetailsLoadCts is not null)
            await _archiveDetailsLoadCts.CancelAsync();

        _archiveDetailsLoadCts?.Dispose();
        var loadCts = new CancellationTokenSource();
        _archiveDetailsLoadCts = loadCts;
        var cancellationToken = loadCts.Token;
        var selectedArchiveId = archive?.Id;

        SelectedArchiveFileGroups.Clear();
        FilteredSelectedArchiveFileGroups.Clear();
        OnPropertyChanged(nameof(ArchiveFileListSummary));
        if (archive is null)
            return;

        IReadOnlyList<ArchiveFileGroupViewModel> builtGroups;
        try
        {
            builtGroups = await BuildSelectedArchiveFileGroupsAsync(
                    archive, SelectedAssetLibrary?.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await UiThread.RunAsync(() =>
        {
            if (_archiveDetailsLoadCts != loadCts || SelectedInstalledArchive?.Id != selectedArchiveId)
                return;

            foreach (var group in builtGroups)
                SelectedArchiveFileGroups.Add(group);

            ApplyArchiveFileFilter();
            OnPropertyChanged(nameof(ArchiveFileListSummary));
        });
    }

    private async Task<IReadOnlyList<ArchiveFileGroupViewModel>> BuildSelectedArchiveFileGroupsAsync(
        InstalledArchiveViewModel archive, Guid? assetLibraryId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var resolvedAssetLibraryId = assetLibraryId
                                     ?? await dbContext.Archives
                                         .AsNoTracking()
                                         .Where(x => x.Id == archive.Id)
                                         .Select(x => x.AssetLibraryId)
                                         .FirstAsync(cancellationToken)
                                         ;

        var archiveIds = await GetArchiveTreeIdsForLibraryAsync(
                dbContext, resolvedAssetLibraryId, archive.Id, cancellationToken);
        var archivesInTree = await archiveFileDetailsService
            .GetArchivesInTreeAsync(dbContext, archiveIds, cancellationToken);
        var records = await archiveFileDetailsService
            .GetInstallRecordRowsAsync(dbContext, archiveIds, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var recordsByArchiveId = records
            .GroupBy(x => x.ArchiveId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var groups = new List<ArchiveFileGroupViewModel>();

        foreach (var archiveEntity in archivesInTree
                     .OrderBy(x => x.Id == archive.Id ? 0 : 1)
                     .ThenBy(x => ArchiveDisplayName.GetEffectiveDisplayName(x.ArchiveName, x.DisplayName),
                         StringComparer.OrdinalIgnoreCase))
        {
            if (!recordsByArchiveId.TryGetValue(archiveEntity.Id, out var archiveRecords))
                continue;

            var group = new ArchiveFileGroupViewModel
            {
                ArchiveId = archiveEntity.Id,
                ArchiveName = archiveEntity.ArchiveName,
                DisplayName = archiveEntity.DisplayName,
                IsSubArchive = archiveEntity.ParentArchiveId is not null,
                IsExpanded = archiveEntity.ParentArchiveId is null
            };

            foreach (var record in archiveRecords
                         .OrderBy(x => x.InstalledRelativePath, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.InstalledAt))
            {
                group.Files.Add(new ArchiveFileRecordViewModel
                {
                    ArchiveId = archiveEntity.Id,
                    OwnerArchiveName = archiveEntity.ArchiveName,
                    OwnerDisplayName = archiveEntity.DisplayName,
                    ArchiveRelativePath = record.ArchiveRelativePath,
                    InstalledRelativePath = record.InstalledRelativePath,
                    FileHash = record.FileHash,
                    FileSize = record.FileSize,
                    Status = record.InstallRecordStatus,
                    IsOverridden = record.HasBeenOverriden,
                    InstalledAt = record.InstalledAt
                });
            }

            groups.Add(group);
        }

        return ArchiveFileGroupBuilder
            .Build(groups, archive.Id, archive.ArchiveName, archive.DisplayName)
            .ToList();
    }

    private void RefreshAvailableCategoryFilters()
    {
        var previousSelection = SelectedCategoryFilter;
        AvailableCategoryFilters.Clear();
        AvailableCategoryFilters.Add(AllCategoriesLabel);

        foreach (var category in InstalledArchives
                     .SelectMany(x => x.Categories)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            AvailableCategoryFilters.Add(category);
        }

        SyncSelectedCategoryFilter(previousSelection);
    }

    private void SyncSelectedCategoryFilter(string? preferredSelection)
    {
        var selected = AvailableCategoryFilters.FirstOrDefault(x =>
                           !string.IsNullOrEmpty(preferredSelection)
                           && string.Equals(x, preferredSelection, StringComparison.OrdinalIgnoreCase))
                       ?? AvailableCategoryFilters[0];

        if (string.Equals(SelectedCategoryFilter, selected, StringComparison.Ordinal))
            OnPropertyChanged(nameof(SelectedCategoryFilter));
        else
            SelectedCategoryFilter = selected;
    }

    private void ApplyInstalledArchiveFilter()
    {
        FilteredInstalledArchives.Clear();

        foreach (var archive in GetFilteredInstalledArchives())
            FilteredInstalledArchives.Add(archive);
    }

    private async Task ApplyInstalledArchiveFilterAsync(CancellationToken cancellationToken)
    {
        FilteredInstalledArchives.Clear();
        await AddToCollectionInBatchesAsync(
            FilteredInstalledArchives,
            GetFilteredInstalledArchives(),
            cancellationToken);
    }

    private IEnumerable<InstalledArchiveViewModel> GetFilteredInstalledArchives()
    {
        var query = InstalledArchiveSearchText.Trim();
        var hasCategoryFilter = !string.Equals(SelectedCategoryFilter, AllCategoriesLabel, StringComparison.Ordinal);

        var matches = InstalledArchives.AsEnumerable();

        if (hasCategoryFilter)
        {
            matches = matches.Where(x =>
                x.Categories.Any(category =>
                    string.Equals(category, SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase)));
        }

        matches = SelectedInstalledArchiveStatusFilter switch
        {
            InstalledArchiveStatusFilter.Uninstalled => matches.Where(x => x.Status == ArchiveStatus.Uninstalled),
            InstalledArchiveStatusFilter.Failed => matches.Where(x => x.Status == ArchiveStatus.Error),
            _ => matches
        };

        if (!string.IsNullOrWhiteSpace(query))
            matches = matches.Where(x => MatchesInstalledArchiveSearch(x, query));

        return InstalledArchiveSorter.Sort(matches, SelectedInstalledArchiveSortOption.Mode);
    }

    private static async Task AddToCollectionInBatchesAsync<T>(
        ObservableCollection<T> collection,
        IEnumerable<T> items,
        CancellationToken cancellationToken)
    {
        var index = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            collection.Add(item);
            index++;

            if (index % CollectionUpdateBatchSize == 0)
                await Task.Yield();
        }
    }

    private void ApplyQueueArchiveFilter()
    {
        FilteredQueueArchives.Clear();

        var query = QueueArchiveSearchText.Trim();
        var matches = (string.IsNullOrWhiteSpace(query)
                ? QueueArchives
                : QueueArchives.Where(x => MatchesQueueArchiveSearch(x, query)))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var archive in matches)
            FilteredQueueArchives.Add(archive);
    }

    private void ApplyArchiveFileFilter()
    {
        FilteredSelectedArchiveFileGroups.Clear();

        var query = ArchiveFileSearchText.Trim();
        var hasQuery = !string.IsNullOrWhiteSpace(query);
        foreach (var group in SelectedArchiveFileGroups)
        {
            group.FilteredFiles.Clear();
            var matches = (hasQuery
                    ? group.Files.Where(x => MatchesArchiveFileSearch(x, query))
                    : group.Files)
                .OrderBy(x => x.InstalledRelativePath, StringComparer.OrdinalIgnoreCase);

            foreach (var file in matches)
                group.FilteredFiles.Add(file);

            if (group.IsSubArchive)
                group.IsExpanded = hasQuery && group.FilteredFiles.Count > 0;

            group.RefreshFilterPresentation();

            if (group.FilteredFiles.Count > 0)
                FilteredSelectedArchiveFileGroups.Add(group);
        }
    }

    private static bool MatchesInstalledArchiveSearch(InstalledArchiveViewModel archive, string query) =>
        InstalledArchiveSearchMatcher.Matches(archive, query);

    private static bool MatchesQueueArchiveSearch(LoadedArchive archive, string query)
    {
        return Contains(archive.DisplayName, query)
               || Contains(archive.ArchivePath, query)
               || Contains(archive.StatusText, query)
               || Contains(archive.ErrorMessage, query)
               || Contains(archive.ContentRoot, query)
               || Contains(archive.ArchiveStatus.ToString(), query)
               || archive.Categories.Any(category => Contains(category, query))
               || archive.AssetTypes.Any(assetType => Contains(assetType.ToString(), query));
    }

    private static bool MatchesArchiveFileSearch(ArchiveFileRecordViewModel file, string query)
    {
        return Contains(file.InstalledRelativePath, query)
               || Contains(file.ArchiveRelativePath, query)
               || Contains(file.OwnerArchiveName, query)
               || Contains(file.OwnerDisplayName, query)
               || Contains(file.OwnerEffectiveDisplayName, query)
               || Contains(file.Status.ToString(), query);
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrEmpty(value)
               && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<Guid>> GetArchiveTreeIdsForLibraryAsync(
        ApplicationDbContext dbContext, Guid assetLibraryId, Guid rootId,
        CancellationToken cancellationToken = default)
    {
        var parentById = await dbContext.Archives
            .AsNoTracking()
            .Where(x => x.AssetLibraryId == assetLibraryId)
            .Select(x => new { x.Id, x.ParentArchiveId })
            .ToDictionaryAsync(x => x.Id, x => x.ParentArchiveId, cancellationToken);

        return ArchiveTreeResolver.GetTreeIds(parentById, rootId);
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

    private string? GetDerivedThumbnailPath(AssetLibrary assetLibrary, Guid archiveId, string? thumbnailFileExtension)
    {
        if (string.IsNullOrWhiteSpace(thumbnailFileExtension))
            return null;

        var thumbnailRoot = assetLibrary.ArchiveThumbnailPath;
        if (string.IsNullOrWhiteSpace(thumbnailRoot))
            thumbnailRoot = settingsService.CurrentSettings.DefaultArchiveThumbnailPath;
        if (string.IsNullOrWhiteSpace(thumbnailRoot))
            thumbnailRoot = installerConfig.ArchiveThumbnailPath;

        return Path.Combine(thumbnailRoot, $"{archiveId}{thumbnailFileExtension}");
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

        loaded.ReplaceClassifications(archive.AssetTypes, archive.Categories);
        return loaded;
    }

    private static List<string> ParseCategories(string categories)
    {
        return categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(DazArchiveScanner.NormalizeCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void SortQueueArchives(ObservableCollection<LoadedArchive> queueArchives)
    {
        queueArchives.SortBy(x => x.DisplayName);
    }

    private async Task RunScanArchiveWorkerAsync(ChannelReader<LoadedArchive> workReader, int total,
        Func<int> incrementCompletedScans)
    {
        await foreach (var archive in workReader.ReadAllAsync())
        {
            await UiThread.RunAsync(() =>
            {
                archive.ArchiveStatus = ArchiveStatus.Loading;
                archive.StatusText = "Scanning archive";
                archive.ProgressPercent = 0;
            });

            try
            {
                var scan = await Task.Run(() => archiveScanner.ScanArchiveAsync(archive.ArchivePath));
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

            var done = incrementCompletedScans();
            _scanProgressCompleted = done;
            if (_uiProgressThrottler.ShouldUpdate(force: done == total))
                await UiThread.RunAsync(() => UpdateScanProgressUi(done, total, force: done == total));
        }
    }

    private void SortQueueArchives()
    {
        SortQueueArchives(QueueArchives);
        ApplyQueueArchiveFilter();
    }
}