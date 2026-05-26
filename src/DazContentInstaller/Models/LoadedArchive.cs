using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using DazContentInstaller.Extensions;
using DazContentInstaller.Database;
using DazContentInstaller.Services;
using DazContentInstaller.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DazContentInstaller.Models;

public partial class LoadedArchive : ViewModelBase
{
    public string ArchivePath { get; }
    public string DisplayName { get; private set; }
    public Guid? ArchiveId { get; set; }
    public ObservableCollection<AssetType> AssetTypes { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];

    [ObservableProperty]
    public partial ArchiveStatus ArchiveStatus { get; set; } = ArchiveStatus.Pending;

    /// <summary>
    /// Set only when a terminal install progress event is yielded. Unlike
    /// <see cref="ArchiveStatus"/>, this is not retroactively changed on older queued progress events.
    /// </summary>
    public bool InstallCompleted { get; private set; }

    public string StatusBadgeText => ArchiveStatus switch
    {
        ArchiveStatus.Pending => "Queued",
        ArchiveStatus.Loading => "Scanning",
        ArchiveStatus.Installing => "Installing",
        _ => ArchiveStatus.ToString()
    };

    partial void OnArchiveStatusChanged(ArchiveStatus value) => OnPropertyChanged(nameof(StatusBadgeText));

    private string? _currentFile;
    public string? CurrentFile
    {
        get => _currentFile;
        set => SetProperty(ref _currentFile, value);
    }

    private string? _statusText;
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private int _processedFiles;
    public int ProcessedFiles
    {
        get => _processedFiles;
        set => SetProperty(ref _processedFiles, value);
    }

    private int _totalFiles;
    public int TotalFiles
    {
        get => _totalFiles;
        set
        {
            if (SetProperty(ref _totalFiles, value))
                OnPropertyChanged(nameof(ScanSummary));
        }
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    private ulong _fileSizeBytes;
    public ulong FileSizeBytes
    {
        get => _fileSizeBytes;
        set
        {
            if (SetProperty(ref _fileSizeBytes, value))
                OnPropertyChanged(nameof(FileSize));
        }
    }

    private ulong _installableSizeBytes;
    public ulong InstallableSizeBytes
    {
        get => _installableSizeBytes;
        set
        {
            if (SetProperty(ref _installableSizeBytes, value))
                OnPropertyChanged(nameof(ScanSummary));
        }
    }

    private string? _contentRoot;
    public string? ContentRoot
    {
        get => _contentRoot;
        set => SetProperty(ref _contentRoot, value);
    }

    public string FileSize => FileSizeFormatter.Format(FileSizeBytes);
    public string ScanSummary => CachedScanResult is null
        ? ArchiveStatus == ArchiveStatus.Pending ? "Queued for scan" : "Scanning…"
        : TotalFiles == 0
            ? "No installable DAZ content found yet"
            : $"{TotalFiles:N0} file(s), {FileSizeFormatter.Format(InstallableSizeBytes)}";

    public ObservableCollection<NestedArchiveSummary> SubArchives { get; } = [];
    public bool HasSubArchives => SubArchives.Count > 0;

    public DazArchiveScanResult? CachedScanResult { get; private set; }

    private bool _suppressInstallProgressNotifications;

    public LoadedArchive(string archivePath)
    {
        ArchivePath = archivePath;
        DisplayName = Path.GetFileName(archivePath);
        var fileInfo = new FileInfo(archivePath);
        FileSizeBytes = fileInfo.Exists ? (ulong)fileInfo.Length : 0UL;
        StatusText = "Waiting to scan";
    }

    public void ApplyScan(DazArchiveScanResult scan)
    {
        ApplyScanMetadata(scan);
        TotalFiles = scan.InstallableFileCount;
        InstallableSizeBytes = scan.InstallableSize;
        ContentRoot = scan.ContentRoot;
        ReplaceClassifications(scan);
        RefreshSubArchives(scan);
        OnPropertyChanged(nameof(ScanSummary));
        ArchiveStatus = TotalFiles == 0 ? ArchiveStatus.Error : ArchiveStatus.Ready;
        ErrorMessage = TotalFiles == 0 ? "No DAZ content directories were found in this archive." : null;
        StatusText = TotalFiles == 0 ? ErrorMessage : "Scanned and ready to install";
        ProgressPercent = TotalFiles == 0 ? 0 : 100;
    }

    public void ResetInstallCompletion() => InstallCompleted = false;

    public void MarkInstallCompleted() => InstallCompleted = true;

    public void SetInstallProgressSilently(
        ArchiveStatus status,
        string? statusText,
        string? errorMessage,
        string? currentFile,
        int processedFiles,
        int totalFiles,
        double progressPercent)
    {
        _suppressInstallProgressNotifications = true;
        try
        {
            InstallCompleted = false;
            ArchiveStatus = status;
            StatusText = statusText;
            ErrorMessage = errorMessage;
            CurrentFile = currentFile;
            ProcessedFiles = processedFiles;
            TotalFiles = totalFiles;
            ProgressPercent = progressPercent;
        }
        finally
        {
            _suppressInstallProgressNotifications = false;
        }
    }

    public void RefreshInstallProgressBindings()
    {
        OnPropertyChanged(nameof(ArchiveStatus));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(CurrentFile));
        OnPropertyChanged(nameof(ProcessedFiles));
        OnPropertyChanged(nameof(TotalFiles));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(ScanSummary));
    }

    public void RefreshQueueItemBindings()
    {
        RefreshInstallProgressBindings();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ContentRoot));
        OnPropertyChanged(nameof(HasSubArchives));
    }

    public void ReplaceClassifications(DazArchiveScanResult scan)
    {
        ReplaceClassifications(
            ArchiveClassification.MergeAssetTypes(scan),
            ArchiveClassification.MergeCategories(scan));
    }

    public void ReplaceClassifications(IReadOnlyList<AssetType> assetTypes, IReadOnlyList<string> categories)
    {
        AssetTypes.Clear();
        foreach (var assetType in assetTypes)
            AssetTypes.Add(assetType);

        Categories.Clear();
        foreach (var category in categories)
            Categories.Add(category);
    }

    public void ApplyScanMetadata(DazArchiveScanResult scan)
    {
        CachedScanResult = scan;
        DisplayName = scan.RootEffectiveDisplayName;
        RefreshSubArchives(scan);
        OnPropertyChanged(nameof(DisplayName));
    }

    private void RefreshSubArchives(DazArchiveScanResult scan)
    {
        SubArchives.Clear();
        if (scan.ContentBearingNestedArchives.Count > 1)
        {
            foreach (var nested in scan.ContentBearingNestedArchives)
                SubArchives.Add(NestedArchiveSummary.FromScanResult(nested));
        }

        OnPropertyChanged(nameof(HasSubArchives));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_suppressInstallProgressNotifications
            && e.PropertyName is nameof(ArchiveStatus)
                or nameof(StatusText)
                or nameof(ErrorMessage)
                or nameof(CurrentFile)
                or nameof(ProcessedFiles)
                or nameof(TotalFiles)
                or nameof(ProgressPercent)
                or nameof(ScanSummary)
                or nameof(StatusBadgeText))
        {
            return;
        }

        base.OnPropertyChanged(e);
    }

    public static List<LoadedArchive> OrderByDisplayName(IEnumerable<LoadedArchive> archives) =>
        archives.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
}
