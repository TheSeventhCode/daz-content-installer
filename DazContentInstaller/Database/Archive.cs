using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace DazContentInstaller.Database;

public class Archive
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    [StringLength(512)]
    public string ArchiveName { get; set; } = null!;
    public long ArchiveSize { get; set; }
    public long FullArchiveSize => AssetFiles.Sum(a => a.FileSize);
    public ArchiveStatus Status { get; set; }
    public List<AssetFile> AssetFiles { get; set; } = [];
    public Guid AssetLibraryId { get; set; }
    public AssetLibrary AssetLibrary { get; set; } = null!;
}