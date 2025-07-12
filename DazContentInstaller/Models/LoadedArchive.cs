using DazContentInstaller.Database;

namespace DazContentInstaller.Models;

public class LoadedArchive
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public ArchiveStatus Status { get; set; }
}