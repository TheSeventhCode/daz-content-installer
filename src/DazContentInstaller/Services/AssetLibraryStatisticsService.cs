using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.Services;

public sealed record AssetLibraryStatistics(
    int ArchiveCount,
    int InstalledArchiveCount,
    int FileCount,
    int ActiveFileCount,
    ulong TotalArchiveSize,
    ulong TotalContentSize);

public interface IAssetLibraryStatisticsService
{
    Task<AssetLibraryStatistics> GetStatisticsAsync(Guid assetLibraryId, CancellationToken cancellationToken = default);
}

public class AssetLibraryStatisticsService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
    : IAssetLibraryStatisticsService
{
    public async Task<AssetLibraryStatistics> GetStatisticsAsync(Guid assetLibraryId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var archiveQuery = dbContext.Archives
            .AsNoTracking()
            .Where(x => x.AssetLibraryId == assetLibraryId && x.ParentArchiveId == null);

        var archiveCount = await archiveQuery.CountAsync(cancellationToken);
        var installedArchiveCount = await archiveQuery
            .CountAsync(x => x.Status == ArchiveStatus.Installed, cancellationToken);
        var totalArchiveSize = archiveCount == 0
            ? 0UL
            : (ulong)await archiveQuery.SumAsync(x => (decimal)x.ArchiveSize, cancellationToken);

        var installRecordQuery = dbContext.InstallRecords
            .AsNoTracking()
            .Where(x => x.Archive.AssetLibraryId == assetLibraryId);

        var fileCount = await installRecordQuery.CountAsync(cancellationToken);
        var activeFileCount = await installRecordQuery
            .CountAsync(InstallRecordActiveFilters.IsActiveOnDiskOwnerExpression, cancellationToken);
        var totalContentSize = fileCount == 0
            ? 0UL
            : (ulong)await installRecordQuery.SumAsync(x => (decimal)x.AssetFile.FileSize, cancellationToken);

        return new AssetLibraryStatistics(
            archiveCount,
            installedArchiveCount,
            fileCount,
            activeFileCount,
            totalArchiveSize,
            totalContentSize);
    }
}
