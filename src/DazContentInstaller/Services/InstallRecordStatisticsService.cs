using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.Services;

public sealed record InstallRecordCounts(
    int FileCount,
    int ActiveFileCount,
    int ReplacedFileCount,
    int SkippedFileCount)
{
    public static InstallRecordCounts Empty { get; } = new(0, 0, 0, 0);

    public InstallRecordCounts Add(InstallRecordCounts other) =>
        new(
            FileCount + other.FileCount,
            ActiveFileCount + other.ActiveFileCount,
            ReplacedFileCount + other.ReplacedFileCount,
            SkippedFileCount + other.SkippedFileCount);
}

public interface IInstallRecordStatisticsService
{
    Task<InstallRecordCounts> GetCountsForArchivesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, InstallRecordCounts>> GetCountsGroupedByArchiveIdAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default);
}

public sealed class InstallRecordStatisticsService : IInstallRecordStatisticsService
{
    public async Task<InstallRecordCounts> GetCountsForArchivesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default)
    {
        if (archiveIds.Count == 0)
            return InstallRecordCounts.Empty;

        var query = dbContext.InstallRecords
            .AsNoTracking()
            .Where(x => archiveIds.Contains(x.ArchiveId));

        return new InstallRecordCounts(
            await query.CountAsync(cancellationToken),
            await query.CountAsync(
                x => !x.HasBeenOverriden && x.InstallRecordStatus != InstallRecordStatus.Uninstalled,
                cancellationToken),
            await query.CountAsync(x => x.InstallRecordStatus == InstallRecordStatus.Replaced, cancellationToken),
            await query.CountAsync(x => x.InstallRecordStatus == InstallRecordStatus.Skipped, cancellationToken));
    }

    public async Task<IReadOnlyDictionary<Guid, InstallRecordCounts>> GetCountsGroupedByArchiveIdAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default)
    {
        if (archiveIds.Count == 0)
            return new Dictionary<Guid, InstallRecordCounts>();

        var groupedCounts = await dbContext.InstallRecords
            .AsNoTracking()
            .Where(x => archiveIds.Contains(x.ArchiveId))
            .GroupBy(x => x.ArchiveId)
            .Select(g => new
            {
                ArchiveId = g.Key,
                FileCount = g.Count(),
                ActiveFileCount = g.Count(x =>
                    !x.HasBeenOverriden && x.InstallRecordStatus != InstallRecordStatus.Uninstalled),
                ReplacedFileCount = g.Count(x => x.InstallRecordStatus == InstallRecordStatus.Replaced),
                SkippedFileCount = g.Count(x => x.InstallRecordStatus == InstallRecordStatus.Skipped)
            })
            .ToListAsync(cancellationToken);

        return groupedCounts.ToDictionary(
            x => x.ArchiveId,
            x => new InstallRecordCounts(
                x.FileCount,
                x.ActiveFileCount,
                x.ReplacedFileCount,
                x.SkippedFileCount));
    }
}
