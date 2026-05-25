using System;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class ArchiveOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Top-level archive this override belongs to.
    /// </summary>
    public Guid RootArchiveId { get; set; }

    public Archive RootArchive { get; set; } = null!;

    public Guid AssetLibraryId { get; set; }

    public AssetLibrary AssetLibrary { get; set; } = null!;

    /// <summary>
    /// Relative directory inside the asset library where the override file lives.
    /// </summary>
    [StringLength(4096)]
    public string InstalledRelativeDirectory { get; set; } = null!;

    [StringLength(1024)]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// App-managed copy of the custom override file.
    /// </summary>
    [StringLength(4096)]
    public string ManagedFilePath { get; set; } = null!;

    /// <summary>
    /// App-managed backup of the original file for replacement overrides.
    /// </summary>
    [StringLength(4096)]
    public string? OriginalFileBackupPath { get; set; }

    public ArchiveOverrideMode Mode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
