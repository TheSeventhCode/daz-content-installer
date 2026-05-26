using System.Collections.Generic;
using System.Linq;
using DazContentInstaller.Database;
using DazContentInstaller.Models;

namespace DazContentInstaller.Services;

public static class QueueProgressAggregator
{
    public static bool IsInstallQueueTerminal(ArchiveStatus status) =>
        status is ArchiveStatus.Installed or ArchiveStatus.Duplicate or ArchiveStatus.Error;

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

        var active = queue.Where(x => x.ArchiveStatus is ArchiveStatus.Loading or ArchiveStatus.Installing).ToList();
        var finishedCount = queue.Count(x => IsInstallQueueTerminal(x.ArchiveStatus));
        var weighted = finishedCount + active.Sum(x => x.ProgressPercent / 100d);
        var percent = weighted / queue.Count * 100d;

        if (active.Count > 0)
        {
            return (percent, $"Installing {finishedCount}/{queue.Count} archives ({active.Count} active)");
        }

        if (finishedCount == queue.Count)
        {
            return (100, BuildInstallFinishedStatus(queue));
        }

        return (percent, $"Installing {finishedCount}/{queue.Count} archives");
    }

    private static string BuildInstallFinishedStatus(IReadOnlyList<LoadedArchive> queue)
    {
        var installed = queue.Count(x => x.ArchiveStatus == ArchiveStatus.Installed);
        var duplicates = queue.Count(x => x.ArchiveStatus == ArchiveStatus.Duplicate);
        var errors = queue.Count(x => x.ArchiveStatus == ArchiveStatus.Error);

        if (duplicates == 0 && errors == 0)
            return "Install queue finished";

        if (errors == 0)
            return $"Install queue finished ({installed} installed, {duplicates} duplicate{(duplicates == 1 ? "" : "s")})";

        if (duplicates == 0)
            return $"Install queue finished ({installed} installed, {errors} failed)";

        return
            $"Install queue finished ({installed} installed, {duplicates} duplicate{(duplicates == 1 ? "" : "s")}, {errors} failed)";
    }
}