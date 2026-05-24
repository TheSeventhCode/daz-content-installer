using System;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class InstallRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ID of the file on disk this record belongs to
    /// </summary>
    public Guid InstalledFileId { get; set; }

    /// <summary>
    /// Actual file on disk this record belongs to
    /// </summary>
    public InstalledFile InstalledFile { get; set; } = null!;

    /// <summary>
    /// When a new installation record was added
    /// </summary>
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// If a newer installation has overriden this version
    /// </summary>
    public bool HasBeenOverriden { get; set; }

    public InstallRecordStatus InstallRecordStatus { get; set; }

    /// <summary>
    /// The ID of the asset file this installation originates from
    /// </summary>
    public Guid AssetFileId { get; set; }

    /// <summary>
    /// The asset file this installation originates from
    /// </summary>
    public AssetFile AssetFile { get; set; } = null!;

    /// <summary>
    /// The ID of the archive this installation originates from
    /// </summary>
    public Guid ArchiveId { get; set; }

    /// <summary>
    /// The archive this installation originates from
    /// </summary>
    public Archive Archive { get; set; } = null!;
}