using System;
using DazContentInstaller.Database;

namespace DazContentInstaller.ViewModels;

public class ArchiveOverrideItemViewModel
{
    public Guid Id { get; init; }
    public string InstalledRelativeDirectory { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string InstalledRelativePath { get; init; } = string.Empty;
    public ArchiveOverrideMode Mode { get; init; }
    public DateTime CreatedAt { get; init; }

    public string ModeLabel => Mode == ArchiveOverrideMode.Replacement ? "Override" : "Addition";
}
