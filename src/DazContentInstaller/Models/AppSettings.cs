using System;

namespace DazContentInstaller.Models;

public class AppSettings
{
    public bool AutoDetectDazLibraries { get; set; } = true;
    public bool CreateBackupBeforeInstall { get; set; } = true;
    public Guid DefaultAssetLibrary { get; set; }
    public string? DefaultArchiveBackupPath { get; set; }
    public string? DefaultArchiveThumbnailPath { get; set; }
    public string? TempWorkingDirectoryPath { get; set; }
    public int MaxConcurrentArchiveInstalls { get; set; } = 2;
}