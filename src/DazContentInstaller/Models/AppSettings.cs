using System;

namespace DazContentInstaller.Models;

public class AppSettings
{
    public bool AutoDetectDazLibraries { get; set; } = true;
    public bool CreateBackupBeforeInstall { get; set; } = true;
    public Guid DefaultAssetLibrary { get; set; }
}