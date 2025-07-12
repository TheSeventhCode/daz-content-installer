using System;

namespace DazContentInstaller.Database;

public class AssetFile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string FileName { get; set; } = null!;
    public long FileSize { get; set; }
    public Guid ArchiveId { get; set; }
    public Archive Archive { get; set; } = null!;
}