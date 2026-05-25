using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.Services;

public sealed record ArchiveTreeArchiveInfo(
    Guid Id,
    string ArchiveName,
    string? DisplayName,
    Guid? ParentArchiveId);

public sealed record ArchiveInstallRecordRow(
    Guid ArchiveId,
    bool HasBeenOverriden,
    InstallRecordStatus InstallRecordStatus,
    DateTime InstalledAt,
    string ArchiveRelativePath,
    string InstalledRelativePath,
    string FileHash,
    ulong FileSize);

public interface IArchiveFileDetailsService
{
    Task<IReadOnlyList<ArchiveTreeArchiveInfo>> GetArchivesInTreeAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveInstallRecordRow>> GetInstallRecordRowsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default);
}

public sealed class ArchiveFileDetailsService : IArchiveFileDetailsService
{
    public async Task<IReadOnlyList<ArchiveTreeArchiveInfo>> GetArchivesInTreeAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default)
    {
        if (archiveIds.Count == 0)
            return [];

        return await dbContext.Archives
            .AsNoTracking()
            .Where(x => archiveIds.Contains(x.Id))
            .Select(x => new ArchiveTreeArchiveInfo(
                x.Id,
                x.ArchiveName,
                x.DisplayName,
                x.ParentArchiveId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveInstallRecordRow>> GetInstallRecordRowsAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<Guid> archiveIds,
        CancellationToken cancellationToken = default)
    {
        if (archiveIds.Count == 0)
            return [];

        return await dbContext.InstallRecords
            .AsNoTracking()
            .Where(x => archiveIds.Contains(x.ArchiveId))
            .Select(x => new ArchiveInstallRecordRow(
                x.ArchiveId,
                x.HasBeenOverriden,
                x.InstallRecordStatus,
                x.InstalledAt,
                x.AssetFile.ArchiveRelativePath,
                x.AssetFile.InstalledRelativePath,
                x.AssetFile.FileHash,
                x.AssetFile.FileSize))
            .ToListAsync(cancellationToken);
    }
}
