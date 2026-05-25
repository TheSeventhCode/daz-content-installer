using System;
using DazContentInstaller.Database;

namespace DazContentInstaller.ViewModels;

public class ArchiveFileRecordViewModel
{
    public Guid ArchiveId { get; init; }
    public string OwnerArchiveName { get; init; } = string.Empty;
    public string? OwnerDisplayName { get; init; }
    public string OwnerEffectiveDisplayName =>
        ArchiveDisplayName.GetEffectiveDisplayName(OwnerArchiveName, OwnerDisplayName);
    public string ArchiveRelativePath { get; init; } = string.Empty;
    public string InstalledRelativePath { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public ulong FileSize { get; init; }
    public InstallRecordStatus Status { get; init; }
    public bool IsOverridden { get; init; }
    public DateTime InstalledAt { get; init; }
}