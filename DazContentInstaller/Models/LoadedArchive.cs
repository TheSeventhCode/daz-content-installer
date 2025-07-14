using System.Collections.Generic;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Models;

public class LoadedArchive : ViewModelBase
{
    private string _name;
    private string _filePath;
    private ArchiveStatus _archiveStatus;
    private long _fileSizeBytes;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public ArchiveStatus ArchiveStatus
    {
        get => _archiveStatus;
        set => SetProperty(ref _archiveStatus, value);
    }

    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set => SetProperty(ref _fileSizeBytes, value);
    }

    public string FileSize => FileSizeFormatter.FormatFileSize(FileSizeBytes);
    public AssetType AssetType { get; set; }
    public HashSet<string> Categories { get; set; } = [];
    public List<AssetFile> ContainedFiles { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = new();

    public LoadedArchive? ParentArchive { get; }

    public string? CustomAssetBaseDirectory { get; }

    public LoadedArchive(string name, string filePath, long fileSize, LoadedArchive? parentArchive = null, string? customAssetBaseDirectory = null)
    {
        _name = name;
        _filePath = filePath;
        _fileSizeBytes = fileSize;
        CustomAssetBaseDirectory = customAssetBaseDirectory;
        ParentArchive = parentArchive;
    }
}