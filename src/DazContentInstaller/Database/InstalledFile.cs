using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class InstalledFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Pure file name
    /// </summary>
    [StringLength(1024)]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// The relative path to the <see cref="AssetLibrary"/> this file is located at
    /// </summary>
    [StringLength(4096)]
    public string InstalledPath { get; set; } = null!;

    /// <summary>
    /// List of installations of this specific file on disk
    /// </summary>
    public List<InstallRecord> InstallRecords { get; set; } = [];

    /// <summary>
    /// ID of the asset library this file is installed to
    /// </summary>
    public Guid AssetLibraryId { get; set; }

    /// <summary>
    /// Asset library this file is installed to
    /// </summary>
    public AssetLibrary AssetLibrary { get; set; } = null!;
}