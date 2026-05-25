using System.IO;

namespace DazContentInstaller.Services;

public class InstallerConfig
{
    public string AppDataPath { get; set; } = null!;
    public string DbPath => Path.Combine(AppDataPath, "database.db");
    public string ArchiveBackupPath => Path.Combine(AppDataPath, "archives");
    public string ArchiveThumbnailPath => Path.Combine(AppDataPath, "thumbnails");
    public string AppSettingsPath => Path.Combine(AppDataPath, "settings.json");
    public int MaxConcurrentArchiveInstalls { get; set; } = 2;
    public int MaxConcurrentScans { get; set; } = 4;
}