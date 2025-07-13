using System.Collections.Generic;
using DazContentInstaller.Database;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Models;

public class LoadedArchive : ViewModelBase
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
    public string FileSize => FormatFileSize(FileSizeBytes);

    public ArchiveStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string StatusColor => Status switch
    {
        ArchiveStatus.Ready => "#28A745",
        ArchiveStatus.Loading => "#FFC107",
        ArchiveStatus.Error => "#DC3545",
        _ => "#6C757D"
    };

    public AssetType AssetType { get; set; } = AssetType.Unknown;
    public HashSet<string> Categories { get; set; } = [];
    public List<AssetFile> ContainedFiles { get; set; } = [];
    public Dictionary<string, object> Metadata { get; set; } = new();
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
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