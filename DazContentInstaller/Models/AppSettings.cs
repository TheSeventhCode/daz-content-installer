namespace DazContentInstaller.Models;

public class AppSettings
{
    public bool AutoDetectDazLibraries { get; set; } = true;
    public bool CreateBackupBeforeInstall { get; set; } = true;
    public string TempDirectory { get; set; } = string.Empty;
}