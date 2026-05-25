using System;
using System.Collections.ObjectModel;
using System.IO;
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
    public partial ArchiveStatus ArchiveStatus { get; set; } = ArchiveStatus.Ready;

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
    public string ScanSummary => TotalFiles == 0
        ? "No installable DAZ content found yet"
        : $"{TotalFiles:N0} file(s), {FileSizeFormatter.Format(InstallableSizeBytes)}";

    public ObservableCollection<NestedArchiveSummary> SubArchives { get; } = [];
    public bool HasSubArchives => SubArchives.Count > 0;

    public DazArchiveScanResult? CachedScanResult { get; private set; }

    public LoadedArchive(string archivePath)
    {
        ArchivePath = archivePath;
        DisplayName = Path.GetFileName(archivePath);
        var fileInfo = new FileInfo(archivePath);
        FileSizeBytes = fileInfo.Exists ? (ulong)fileInfo.Length : 0UL;
        StatusText = "Ready to scan";
    }

    public void ApplyScan(DazArchiveScanResult scan)
    {
        ApplyScanMetadata(scan);
        TotalFiles = scan.InstallableFileCount;
        InstallableSizeBytes = scan.InstallableSize;
        ContentRoot = scan.ContentRoot;
        AssetTypes.Clear();
        foreach (var assetType in scan.AssetTypes)
            AssetTypes.Add(assetType);
        Categories.Clear();
        foreach (var category in scan.Categories)
            Categories.Add(category);
        RefreshSubArchives(scan);
        ArchiveStatus = TotalFiles == 0 ? ArchiveStatus.Error : ArchiveStatus.Ready;
        ErrorMessage = TotalFiles == 0 ? "No DAZ content directories were found in this archive." : null;
        StatusText = TotalFiles == 0 ? ErrorMessage : "Scanned and ready to install";
        ProgressPercent = TotalFiles == 0 ? 0 : 100;
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
}
