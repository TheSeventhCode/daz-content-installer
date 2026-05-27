using System;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class AssetFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The installation record this asset file caused
    /// </summary>
    public InstallRecord? InstallRecord { get; set; }

    /// <summary>
    /// Absolute file size
    /// </summary>
    public ulong FileSize { get; set; }

    /// <summary>
    /// The relative path of this file inside the source archive.
    /// </summary>
    [StringLength(4096)]
    public string ArchiveRelativePath { get; set; } = null!;

    /// <summary>
    /// The relative path this file should be installed to inside the asset library.
    /// </summary>
    [StringLength(4096)]
    public string InstalledRelativePath { get; set; } = null!;

    /// <summary>
    /// Hash of the asset file
    /// </summary>
    [StringLength(1024)] public string FileHash { get; set; } = null!;

    /// <summary>
    /// ID of the <see cref="Archive"/> this asset file was included in
    /// </summary>
    public Guid ArchiveId { get; set; }

    /// <summary>
    /// <see cref="Archive"/> this asset was included in
    /// </summary>
    public Archive Archive { get; set; } = null!;
}