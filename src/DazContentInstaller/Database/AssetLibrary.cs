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
    /// When was this asset library created
    /// </summary>
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.Now;
    
    /// <summary>
    /// When was something last installed or uninstalled from this asset library
    /// </summary>
    public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.Now;
    
    /// <summary>
    /// The archives that were installed into this asset library
    /// </summary>
    public List<Archive> Archives { get; set; } = [];

    /// <summary>
    /// The actual files on disk that are installed into this asset library
    /// </summary>
    public List<InstalledFile> InstalledFiles { get; set; } = [];
}