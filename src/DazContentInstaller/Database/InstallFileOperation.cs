using System;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class InstallFileOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ArchiveId { get; set; }

    public Archive Archive { get; set; } = null!;

    public Guid InstallRecordId { get; set; }

    public InstallRecord InstallRecord { get; set; } = null!;

    public Guid InstalledFileId { get; set; }

    public InstalledFile InstalledFile { get; set; } = null!;

    [StringLength(4096)]
    public string InstalledRelativePath { get; set; } = null!;

    [StringLength(4096)]
    public string? BackupFilePath { get; set; }

    [StringLength(1024)]
    public string? BackupFileHash { get; set; }

    [StringLength(1024)]
    public string NewFileHash { get; set; } = null!;

    public InstallFileOperationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
