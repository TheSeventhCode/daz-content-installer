using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class QueueProgressAggregatorTests
{
    [Fact]
    public void ComputeScanProgress_ReturnsZeroWhenTotalIsZero()
    {
        var (percent, status) = QueueProgressAggregator.ComputeScanProgress(0, 0);

        percent.ShouldBe(0);
        status.ShouldBe("No archives to scan");
    }

    [Fact]
    public void ComputeScanProgress_ComputesPercentAndStatus()
    {
        var (percent, status) = QueueProgressAggregator.ComputeScanProgress(2, 5);

        percent.ShouldBe(40d);
        status.ShouldBe("Scanning 2/5 archives");
    }

    [Fact]
    public void ComputeInstallProgress_ReturnsZeroWhenQueueIsEmpty()
    {
        var (percent, status) = QueueProgressAggregator.ComputeInstallProgress([]);

        percent.ShouldBe(0);
        status.ShouldBe("Nothing to install");
    }

    [Fact]
    public void ComputeInstallProgress_WeightsActiveInstallProgress()
    {
        var queue = new List<LoadedArchive>
        {
            CreateProgressArchive(ArchiveStatus.Installed, 100),
            CreateProgressArchive(ArchiveStatus.Installing, 50),
            CreateProgressArchive(ArchiveStatus.Ready, 0)
        };

        var (percent, status) = QueueProgressAggregator.ComputeInstallProgress(queue);

        percent.ShouldBe(50d);
        status.ShouldBe("Installing 1/3 archives (1 active)");
    }

    [Fact]
    public void ComputeInstallProgress_IncludesLoadingArchivesAsActive()
    {
        var queue = new List<LoadedArchive>
        {
            CreateProgressArchive(ArchiveStatus.Loading, 25),
            CreateProgressArchive(ArchiveStatus.Ready, 0)
        };

        var (percent, status) = QueueProgressAggregator.ComputeInstallProgress(queue);

        percent.ShouldBe(12.5d);
        status.ShouldBe("Installing 0/2 archives (1 active)");
    }

    [Fact]
    public void ComputeInstallProgress_ReportsFinishedWhenAllInstalled()
    {
        var queue = new List<LoadedArchive>
        {
            CreateProgressArchive(ArchiveStatus.Installed, 100),
            CreateProgressArchive(ArchiveStatus.Installed, 100)
        };

        var (percent, status) = QueueProgressAggregator.ComputeInstallProgress(queue);

        percent.ShouldBe(100d);
        status.ShouldBe("Install queue finished");
    }

    private static LoadedArchive CreateProgressArchive(ArchiveStatus status, double progressPercent)
    {
        var archive = new LoadedArchive(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip"))
        {
            ArchiveStatus = status,
            ProgressPercent = progressPercent
        };
        return archive;
    }
}