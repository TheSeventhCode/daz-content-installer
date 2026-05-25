using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class Archive
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Detected name of the archive file
    /// </summary>
    [StringLength(512)]
    public string ArchiveName { get; set; } = null!;

    /// <summary>
    /// Size of the zip file.
    /// </summary>
    public ulong ArchiveSize { get; set; }
    
    // public string? CustomAssetsBasePath { get; set; }
    
    /// <summary>
    /// Current installation status
    /// </summary>
    public ArchiveStatus Status { get; set; }

    /// <summary>
    /// The relative content root detected inside the archive that maps to the asset library.
    /// </summary>
    [StringLength(4096)]
    public string? ContentRoot { get; set; }

    /// <summary>
    /// Comma-separated asset types detected while scanning the archive.
    /// </summary>
    [StringLength(512)]
    public string AssetTypes { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated folder labels detected from the archive's folder structure.
    /// </summary>
    [StringLength(2048)]
    public string Categories { get; set; } = string.Empty;

    /// <summary>
    /// When installation of this archive started.
    /// </summary>
    public DateTime? InstallStartedAt { get; set; }

    /// <summary>
    /// When installation of this archive completed.
    /// </summary>
    public DateTime? InstallCompletedAt { get; set; }

    /// <summary>
    /// Error message captured for the latest failed install attempt.
    /// </summary>
    [StringLength(4096)]
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Files part of this archive
    /// </summary>
    public List<AssetFile> AssetFiles { get; set; } = [];
    
    /// <summary>
    /// ID of the asset library this archive is part of
    /// </summary>
    public Guid AssetLibraryId { get; set; }
    /// <summary>
    /// Asset library this archive is part of
    /// </summary>
    public AssetLibrary AssetLibrary { get; set; } = null!;
    
    /// <summary>
    /// Potential sub archives that were included in this archive 
    /// </summary>
    public List<Archive> SubArchives { get; set; } = [];

    /// <summary>
    /// Potential parent archive id this archive was included in
    /// </summary>
    public Guid? ParentArchiveId { get; set; }
    
    /// <summary>
    /// Potential parent archive this archive was included in
    /// </summary>
    public Archive? ParentArchive { get; set; }
}