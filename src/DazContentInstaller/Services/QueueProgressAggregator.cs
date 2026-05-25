using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;
using DazContentInstaller.Models;

namespace DazContentInstaller.Services;

public static class QueueProgressAggregator
{
    public static (double Percent, string StatusText) ComputeScanProgress(int completed, int total)
    {
        if (total == 0)
            return (0, "No archives to scan");

        var percent = (double)completed / total * 100d;
        return (percent, $"Scanning {completed}/{total} archives");
    }

    public static (double Percent, string StatusText) ComputeInstallProgress(IReadOnlyList<LoadedArchive> queue)
    {
        if (queue.Count == 0)
            return (0, "Nothing to install");

        var completed = queue.Count(x => x.ArchiveStatus == ArchiveStatus.Installed);
        var active = queue.Where(x => x.ArchiveStatus is ArchiveStatus.Loading or ArchiveStatus.Installing).ToList();
        var weighted = completed + active.Sum(x => x.ProgressPercent / 100d);
        var percent = weighted / queue.Count * 100d;

        var status = active.Count > 0
            ? $"Installing {completed}/{queue.Count} archives ({active.Count} active)"
            : completed == queue.Count
                ? "Install queue finished"
                : $"Installing {completed}/{queue.Count} archives";

        return (percent, status);
    }
}
