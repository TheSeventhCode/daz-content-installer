using DazContentInstaller.Database;

namespace DazContentInstaller.Models;

public class LoadedArchive
{
    private readonly string _archivePath;
    public ArchiveStatus ArchiveStatus { get; set; }

    public LoadedArchive(string archivePath)
    {
        _archivePath = archivePath;
    }
}