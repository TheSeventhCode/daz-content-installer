using System;
using DazContentInstaller.Database;

namespace DazContentInstaller.ViewModels;

public class ArchiveFileRecordViewModel
{
    public string ArchiveRelativePath { get; init; } = string.Empty;
    public string InstalledRelativePath { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public ulong FileSize { get; init; }
    public InstallRecordStatus Status { get; init; }
    public bool IsOverridden { get; init; }
    public DateTime InstalledAt { get; init; }
}