using System.IO;

namespace DazContentInstaller.Options;

public class InstallerConfig
{
    public string AppDataPath { get; set; } = null!;
    public string DbPath => Path.Combine(AppDataPath, "database.db");
    public string ArchiveBackupPath => Path.Combine(AppDataPath, "archives");
    public string AppSettingsPath => Path.Combine(AppDataPath, "settings.json");
}