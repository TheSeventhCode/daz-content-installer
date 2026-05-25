using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class AssetLibrary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Name of the asset library
    /// <example>General, Characters, Environments, etc.</example>
    /// </summary>
    [StringLength(512)] public string Name { get; set; } = null!;
    
    /// <summary>
    /// Path on the system for this asset library
    /// <example>~/Documents/DAZ 3D/Studio/My Library/</example>
    /// </summary>
    [StringLength(4096)] public string Path { get; set; } = null!;

    /// <summary>
    /// Optional override for where original archives should be backed up for this library.
    /// </summary>
    [StringLength(4096)]
    public string? ArchiveBackupPath { get; set; }

    /// <summary>
    /// When was this asset library created
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    
    /// <summary>
    /// When was something last installed or uninstalled from this asset library
    /// </summary>
    public DateTime LastUsed { get; set; } = DateTime.Now;
    
    /// <summary>
    /// The archives that were installed into this asset library
    /// </summary>
    public List<Archive> Archives { get; set; } = [];

    /// <summary>
    /// The actual files on disk that are installed into this asset library
    /// </summary>
    public List<InstalledFile> InstalledFiles { get; set; } = [];
}