using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DazContentInstaller.Database;

namespace DazContentInstaller.Services;

public sealed record ArchiveOverrideInfo(
    Guid Id,
    string InstalledRelativeDirectory,
    string FileName,
    ArchiveOverrideMode Mode,
    DateTime CreatedAt)
{
    public string InstalledRelativePath =>
        string.IsNullOrEmpty(InstalledRelativeDirectory)
            ? FileName
            : Path.Combine(InstalledRelativeDirectory, FileName);
}

public interface IArchiveOverrideService
{
    Task<bool> HasOverridesAsync(Guid rootArchiveId, CancellationToken cancellationToken = default);

    Task<int> GetOverrideCountAsync(Guid rootArchiveId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveOverrideInfo>> GetOverridesAsync(
        Guid rootArchiveId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCandidateDirectoriesAsync(
        Guid rootArchiveId,
        CancellationToken cancellationToken = default);

    Task AddOverridesAsync(
        Guid rootArchiveId,
        string installedRelativeDirectory,
        IReadOnlyList<string> sourceFilePaths,
        CancellationToken cancellationToken = default);

    Task DeleteOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default);
}
