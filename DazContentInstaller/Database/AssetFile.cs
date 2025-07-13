using System;
using System.ComponentModel.DataAnnotations;

namespace DazContentInstaller.Database;

public class AssetFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    [StringLength(1024)] public string FileName { get; set; } = null!;

    [StringLength(1024)] public string? InstalledPath { get; set; }
    public ulong FileSize { get; set; }
    public Guid ArchiveId { get; set; }
    public Archive Archive { get; set; } = null!;
}