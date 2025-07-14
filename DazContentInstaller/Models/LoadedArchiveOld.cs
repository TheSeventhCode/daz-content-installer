using System.Collections.Generic;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Models;

public class LoadedArchiveOld : ViewModelBase
{
    private string _name = string.Empty;
    private string _filePath = string.Empty;
    private ArchiveStatus _status;
    private long _fileSizeBytes;

    public bool IsPartOfParentArchive { get; set; }
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string? CustomAssetBaseDirectory { get; set; }
    public string? CustomSubArchiveDirectory { get; set; }

    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set => SetProperty(ref _fileSizeBytes, value);
    }

    public string FileSize => FileSizeFormatter.FormatFileSize(FileSizeBytes);

    public ArchiveStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public AssetType AssetType { get; set; } = AssetType.Unknown;
    public HashSet<string> Categories { get; set; } = [];
    public List<AssetFile> ContainedFiles { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = new();
}