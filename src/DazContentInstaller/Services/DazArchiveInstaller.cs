using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;
using DazContentInstaller.Models;
using Microsoft.EntityFrameworkCore;
using SharpCompress.Archives;

namespace DazContentInstaller.Services;

public interface IDazArchiveInstaller
{
    IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(Guid assetLibraryId, IEnumerable<LoadedArchive> archives,
        CancellationToken cancellationToken = default);

    Task UninstallArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<LoadedArchive> ReinstallArchiveAsync(Guid archiveId, LoadedArchive archive,
        CancellationToken cancellationToken = default);

    Task ForgetArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default);
}

public class DazArchiveInstaller(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    SettingsService settingsService,
    InstallerConfig installerConfig,
    IDirectoryService directoryService,
    IDazArchiveScanner archiveScanner,
    DestinationPathLockRegistry destinationPathLockRegistry,
    ArchiveIdentityLockRegistry archiveIdentityLockRegistry,
    ArchiveInstallCoordinator archiveInstallCoordinator,
    IArchiveOverrideService archiveOverrideService)
    : IDazArchiveInstaller
{
    private static readonly TimeSpan InstallProgressYieldInterval = TimeSpan.FromMilliseconds(100);

    public async IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(Guid assetLibraryId,
        IEnumerable<LoadedArchive> archives,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var archiveList = LoadedArchive.OrderByDisplayName(archives);
        if (archiveList.Count == 0)
            yield break;

        await foreach (var progress in RunInstallWorkersAsync(assetLibraryId, archiveList, cancellationToken))
            yield return progress;
    }

    public async Task UninstallArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default)
    {
        if (await archiveOverrideService.HasOverridesAsync(archiveId, cancellationToken))
        {
            var overrideCount = await archiveOverrideService
                .GetOverrideCountAsync(archiveId, cancellationToken);
            throw new InvalidOperationException(
                $"Cannot uninstall this archive while {overrideCount} override(s) remain. Remove overrides first.");
        }

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, archiveId, cancellationToken);
        var installRecords = await dbContext.InstallRecords
            .Include(x => x.InstalledFile)
            .ThenInclude(x => x.AssetLibrary)
            .Where(x => archiveIds.Contains(x.ArchiveId)
                        && !x.HasBeenOverriden
                        && x.InstallRecordStatus != InstallRecordStatus.Uninstalled)
            .ToListAsync(cancellationToken);

        var filesToDelete = new List<(string FullPath, string LibraryRoot)>();
        var uninstallRecordIds = installRecords.Select(x => x.Id).ToHashSet();
        var installedFileIds = installRecords.Select(x => x.InstalledFileId).Distinct().ToList();
        var remainingActiveOwnerFileIds = (await dbContext.InstallRecords
                .Where(x => installedFileIds.Contains(x.InstalledFileId)
                            && !uninstallRecordIds.Contains(x.Id)
                            && !x.HasBeenOverriden
                            && (x.InstallRecordStatus == InstallRecordStatus.Installed ||
                                x.InstallRecordStatus == InstallRecordStatus.Replaced ||
                                x.InstallRecordStatus == InstallRecordStatus.Skipped))
                .Select(x => x.InstalledFileId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var record in installRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            record.InstallRecordStatus = InstallRecordStatus.Uninstalled;
            record.HasBeenOverriden = true;
            record.InstalledFile.AssetLibrary.LastUsed = DateTime.Now;
        }

        foreach (var group in installRecords.GroupBy(x => x.InstalledFileId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var representative = group.First();
            var hasRemainingActiveOwners = remainingActiveOwnerFileIds.Contains(group.Key);

            if (hasRemainingActiveOwners)
                continue;

            var libraryRoot = GetCanonicalPath(representative.InstalledFile.AssetLibrary.Path);
            var fullPath = Path.Combine(libraryRoot, representative.InstalledFile.InstalledPath,
                representative.InstalledFile.FileName);
            var fileExists = File.Exists(fullPath);

            if (fileExists)
                filesToDelete.Add((fullPath, libraryRoot));
        }

        if (filesToDelete.Count > 0)
        {
            await Task.Run(() =>
            {
                foreach (var (fullPath, libraryRoot) in filesToDelete)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(fullPath);
                    RemoveEmptyDirectories(Path.GetDirectoryName(fullPath), libraryRoot);
                }
            }, cancellationToken);
        }

        var archives = await dbContext.Archives.Where(x => archiveIds.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach (var archive in archives)
        {
            archive.Status = ArchiveStatus.Uninstalled;
            archive.InstallCompletedAt = DateTime.Now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async IAsyncEnumerable<LoadedArchive> ReinstallArchiveAsync(Guid archiveId, LoadedArchive archive,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var archiveEntity = await dbContext.Archives
            .Include(x => x.AssetLibrary)
            .FirstOrDefaultAsync(x => x.Id == archiveId && x.ParentArchiveId == null, cancellationToken);

        if (archiveEntity is null)
            throw new InvalidOperationException("Archive not found.");

        if (archiveEntity.Status != ArchiveStatus.Uninstalled)
            throw new InvalidOperationException("Only uninstalled archives can be reinstalled.");

        var backupPath = Path.Combine(GetBackupRoot(archiveEntity.AssetLibrary), archiveEntity.ArchiveName);
        if (!File.Exists(backupPath))
            throw new InvalidOperationException("Archive backup file not found.");

        if (!string.Equals(archive.ArchivePath, backupPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Archive path does not match the backup file for this archive.");

        await PrepareExistingArchiveForReinstallAsync(dbContext, archiveEntity, cancellationToken);

        archive.ArchiveId = archiveEntity.Id;

        await foreach (var progress in RunSingleArchiveInstallAsync(archiveEntity.AssetLibraryId, archive,
                           archiveEntity.Id, cancellationToken))
            yield return progress;
    }

    public async Task ForgetArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, archiveId, cancellationToken);
        var stillInstalled = await dbContext.Archives
            .AnyAsync(x => archiveIds.Contains(x.Id)
                           && x.Status != ArchiveStatus.Uninstalled
                           && x.Status != ArchiveStatus.Error,
                cancellationToken);
        if (stillInstalled)
            throw new InvalidOperationException("Only uninstalled or failed archives can be fully removed.");

        var installedFileIds = await dbContext.InstallRecords
            .Where(x => archiveIds.Contains(x.ArchiveId))
            .Select(x => x.InstalledFileId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var rootArchive = await dbContext.Archives
            .Include(x => x.AssetLibrary)
            .FirstOrDefaultAsync(x => x.Id == archiveId, cancellationToken);
        if (rootArchive is not null)
            dbContext.Archives.Remove(rootArchive);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (rootArchive is not null)
            await DeleteArchiveThumbnailAsync(rootArchive.AssetLibrary, rootArchive, cancellationToken);

        var orphanedFiles = await dbContext.InstalledFiles
            .Include(x => x.InstallRecords)
            .Where(x => installedFileIds.Contains(x.Id) && !x.InstallRecords.Any())
            .ToListAsync(cancellationToken);

        dbContext.InstalledFiles.RemoveRange(orphanedFiles);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async IAsyncEnumerable<LoadedArchive> RunInstallWorkersAsync(Guid assetLibraryId,
        IReadOnlyList<LoadedArchive> archiveList, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var progressChannel = Channel.CreateUnbounded<LoadedArchive>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var workChannel = Channel.CreateUnbounded<LoadedArchive>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

        foreach (var archive in archiveList)
            await workChannel.Writer.WriteAsync(archive, cancellationToken);

        workChannel.Writer.Complete();

        var workerCount = Math.Max(1,
            Math.Min(archiveInstallCoordinator.MaxConcurrentArchiveInstalls, archiveList.Count));
        var workerTasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(
                () => RunInstallWorkerLoopAsync(assetLibraryId, workChannel.Reader, progressChannel.Writer,
                    cancellationToken),
                cancellationToken))
            .ToArray();

        var completionTask = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(workerTasks);
            }
            finally
            {
                progressChannel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var progress in progressChannel.Reader.ReadAllAsync(cancellationToken))
            yield return progress;

        await completionTask;
    }

    private async IAsyncEnumerable<LoadedArchive> RunSingleArchiveInstallAsync(Guid assetLibraryId,
        LoadedArchive archive, Guid existingArchiveId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<LoadedArchive>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var workerTask = Task.Run(ArchiveTask, cancellationToken);

        await foreach (var progress in channel.Reader.ReadAllAsync(cancellationToken))
            yield return progress;

        await workerTask;
        yield break;

        async Task? ArchiveTask()
        {
            try
            {
                await using (await archiveInstallCoordinator.AcquireArchiveInstallSlotAsync(cancellationToken))
                {
                    await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                    var assetLibrary = await dbContext.AssetLibraries
                        .FirstAsync(x => x.Id == assetLibraryId, cancellationToken);
                    var existingArchiveEntity = await dbContext.Archives.Include(x => x.AssetLibrary)
                        .FirstAsync(x => x.Id == existingArchiveId, cancellationToken);

                    await foreach (var progress in InstallArchiveAsync(dbContext, assetLibrary, archive,
                                       existingArchiveEntity, cancellationToken))
                        await channel.Writer.WriteAsync(progress, cancellationToken);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }
    }

    private async Task RunInstallWorkerLoopAsync(Guid assetLibraryId, ChannelReader<LoadedArchive> workReader,
        ChannelWriter<LoadedArchive> progressWriter, CancellationToken cancellationToken)
    {
        await foreach (var archive in workReader.ReadAllAsync(cancellationToken))
        {
            await using (await archiveInstallCoordinator.AcquireArchiveInstallSlotAsync(cancellationToken))
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var assetLibrary =
                    await dbContext.AssetLibraries.FirstAsync(x => x.Id == assetLibraryId, cancellationToken);

                await foreach (var progress in InstallArchiveAsync(dbContext, assetLibrary, archive,
                                   cancellationToken: cancellationToken))
                    await progressWriter.WriteAsync(progress, cancellationToken);
            }
        }
    }

    private async IAsyncEnumerable<LoadedArchive> InstallArchiveAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary,
        LoadedArchive archive, Archive? existingArchiveEntity = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var progressYieldState = new ProgressYieldState();
        var progressThrottler = new ProgressUpdateThrottler(InstallProgressYieldInterval);

        BeginArchiveInstallProgress(archive);
        if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
            yield return archive;

        var sourceInfo = new FileInfo(archive.ArchivePath);
        if (PublishMissingArchiveFile(archive, sourceInfo))
        {
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        var preparedArchive = await PrepareArchiveForInstallAsync(dbContext, assetLibrary, archive,
            sourceInfo, existingArchiveEntity, cancellationToken);
        if (preparedArchive.IsDuplicate)
        {
            PublishDuplicateArchiveProgress(archive, "An archive with the same name and size is already tracked for this library.");
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        var archiveEntity = preparedArchive.ArchiveEntity
                            ?? throw new InvalidOperationException("Archive entity was not prepared for installation.");

        var scanResult = await GetScanResultForInstallAsync(sourceInfo, archive, preparedArchive.IsResumed,
            cancellationToken);
        if (scanResult.Failure is not null)
        {
            await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, scanResult.Failure, cancellationToken);
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        var resolvedScanResult = scanResult.Result
                                 ?? throw new InvalidOperationException("Archive scan did not return a result.");

        ApplyScanResultToInstall(dbContext, archiveEntity, archive, resolvedScanResult);
        if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
            yield return archive;

        if (await FailIfNoScannedContentAsync(dbContext, archiveEntity, archive, resolvedScanResult, cancellationToken))
        {
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        using var workingDirectory = directoryService.GetTempDirectory();
        var (installableEntries, exception) = await CollectInstallableEntriesForInstallAsync(dbContext, assetLibrary,
                archiveEntity, sourceInfo, workingDirectory.DirectoryInfo.FullName, resolvedScanResult, cancellationToken);
        if (exception is not null)
        {
            await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, exception,
                    cancellationToken);
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        PublishArchiveProgress(archive, ArchiveStatus.Loading, "Collecting installable files", processedFiles: 0,
            totalFiles: installableEntries.Count);

        if (await FailIfNoInstallableEntriesAsync(dbContext, archiveEntity, archive, installableEntries.Count,
                cancellationToken))
        {
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        ApplyArchiveFingerprints(installableEntries, archiveEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (await RejectDuplicateContentAsync(dbContext, assetLibrary, archiveEntity, archive,
                preparedArchive.IsResumed, cancellationToken))
        {
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        if (ShouldCreateInstallBackup(preparedArchive.IsResumed))
        {
            PublishArchiveProgress(archive, ArchiveStatus.Loading, "Creating archive backup");
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;

            var backupError = await TryBackupArchiveAsync(assetLibrary, sourceInfo.FullName, cancellationToken);
            if (backupError is not null)
            {
                await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, backupError, cancellationToken);
                if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                    yield return archive;
                yield break;
            }
        }

        await TryRefreshArchiveThumbnailAsync(assetLibrary, sourceInfo.FullName, archiveEntity, resolvedScanResult,
            cancellationToken);

        await PrepopulateInstallManifestAsync(dbContext, assetLibrary, installableEntries, cancellationToken);

        await BeginFileInstallAsync(dbContext, archiveEntity, archive, installableEntries.Count, cancellationToken);
        if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
            yield return archive;

        var installSummary = new InstallEntriesSummary();
        await foreach (var progress in InstallEntriesAsync(dbContext, assetLibrary, archive, installableEntries,
                           progressThrottler, installSummary, progressYieldState, cancellationToken))
            yield return progress;

        if (installSummary.Failure is not null)
        {
            await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, installSummary.Failure, cancellationToken);
            if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
                yield return archive;
            yield break;
        }

        await CompleteArchiveInstallAsync(dbContext, assetLibrary, archiveEntity, archive, installableEntries.Count,
            installSummary.SkippedDuplicateCount, cancellationToken);
        if (await YieldArchiveProgressAsync(archive, progressYieldState, force: true))
            yield return archive;
    }

    private static void BeginArchiveInstallProgress(LoadedArchive archive)
    {
        archive.ResetInstallCompletion();
        PublishArchiveProgress(archive, ArchiveStatus.Loading, "Preparing archive", errorMessage: null,
            progressPercent: 0);
    }

    private static bool PublishMissingArchiveFile(LoadedArchive archive, FileInfo sourceInfo)
    {
        if (sourceInfo.Exists)
            return false;

        PublishArchiveProgress(archive, ArchiveStatus.Error, "Archive file not found.",
            errorMessage: "Archive file not found.", progressPercent: 100);
        return true;
    }

    private async Task<PreparedArchiveEntityResult> PrepareArchiveForInstallAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary, LoadedArchive archive, FileInfo sourceInfo, Archive? existingArchiveEntity,
        CancellationToken cancellationToken)
    {
        var preparedArchive = existingArchiveEntity is null
            ? await PrepareNewArchiveForInstallAsync(dbContext, assetLibrary, sourceInfo, cancellationToken)

            : new PreparedArchiveEntityResult(existingArchiveEntity, IsDuplicate: false, IsResumed: true);

        if (preparedArchive.ArchiveEntity is not null)
            await ResetArchiveEntityForInstallAsync(dbContext, preparedArchive.ArchiveEntity, archive, cancellationToken);

        return preparedArchive;
    }

    private async Task<PreparedArchiveEntityResult> PrepareNewArchiveForInstallAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary, FileInfo sourceInfo, CancellationToken cancellationToken)
    {
        var archiveIdentityKey = ArchiveIdentityLockRegistry.CreateLockKey(
            assetLibrary.Id, sourceInfo.Name, (ulong)sourceInfo.Length);

        return await archiveIdentityLockRegistry.ExecuteAsync(archiveIdentityKey,
                () => PrepareArchiveEntityForInstallAsync(dbContext, assetLibrary, sourceInfo, cancellationToken),
                cancellationToken);
    }

    private static async Task ResetArchiveEntityForInstallAsync(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive, CancellationToken cancellationToken)
    {
        archiveEntity.Status = ArchiveStatus.Loading;
        archiveEntity.ErrorMessage = null;
        archiveEntity.InstallStartedAt = DateTime.Now;
        archiveEntity.InstallCompletedAt = null;

        await dbContext.SaveChangesAsync(cancellationToken);
        archive.ArchiveId = archiveEntity.Id;
    }

    private async Task<ArchiveScanAttempt> GetScanResultForInstallAsync(FileInfo sourceInfo, LoadedArchive archive,
        bool isResumedInstall, CancellationToken cancellationToken)
    {
        var cachedScanResult = isResumedInstall ? null : archive.CachedScanResult;
        if (cachedScanResult is not null)
            return new ArchiveScanAttempt(cachedScanResult, null);

        PublishArchiveProgress(archive, ArchiveStatus.Loading, "Scanning archive");

        try
        {
            var scanResult = await archiveScanner.ScanArchiveAsync(sourceInfo.FullName, cancellationToken);
            return new ArchiveScanAttempt(scanResult, null);
        }
        catch (Exception ex)
        {
            return new ArchiveScanAttempt(null, ex);
        }
    }

    private static void ApplyScanResultToInstall(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive, DazArchiveScanResult scanResult)
    {
        archiveEntity.ContentRoot = scanResult.ContentRoot;
        archiveEntity.DisplayName = scanResult.ResolveRootDisplayName();
        archiveEntity.AssetTypes = AssetTypeCollection.Serialize(scanResult.AssetTypes);
        archiveEntity.Categories = SerializeCategories(scanResult.Categories);
        archive.InstallableSizeBytes = scanResult.InstallableSize;
        archive.ContentRoot = scanResult.ContentRoot;
        archive.ApplyScanMetadata(scanResult);
        archive.ReplaceClassifications(scanResult);
        PublishArchiveProgress(archive, ArchiveStatus.Loading, "Scan complete",
            totalFiles: scanResult.InstallableFileCount,
            progressPercent: 0);
        dbContext.ChangeTracker.DetectChanges();
    }

    private static async Task<bool> FailIfNoScannedContentAsync(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive, DazArchiveScanResult scanResult, CancellationToken cancellationToken)
    {
        if (scanResult.InstallableFileCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        await MarkArchiveWithNoContentAsync(dbContext, archiveEntity, archive, totalFiles: 0,
            cancellationToken);
        return true;
    }

    private async Task<InstallableEntryCollectionResult> CollectInstallableEntriesForInstallAsync(
        ApplicationDbContext dbContext, AssetLibrary assetLibrary, Archive archiveEntity, FileInfo sourceInfo,
        string workingDirectory, DazArchiveScanResult scanResult, CancellationToken cancellationToken)
    {
        var installableEntries = new List<InstallableArchiveEntry>();
        var libraryRoot = GetCanonicalPath(assetLibrary.Path);

        try
        {
            using var sourceArchive = await Task
                .Run(() => ArchiveFactory.OpenArchive(sourceInfo.FullName), cancellationToken);
            await CollectInstallableEntriesAsync(dbContext, sourceArchive, archiveEntity,
                workingDirectory, installableEntries, libraryRoot, scanResult, cancellationToken);
            return new InstallableEntryCollectionResult(installableEntries, null);
        }
        catch (Exception ex)
        {
            return new InstallableEntryCollectionResult(installableEntries, ex);
        }
    }

    private static async Task<bool> FailIfNoInstallableEntriesAsync(ApplicationDbContext dbContext,
        Archive archiveEntity, LoadedArchive archive, int installableEntryCount, CancellationToken cancellationToken)
    {
        if (installableEntryCount > 0)
            return false;

        await MarkArchiveWithNoContentAsync(dbContext, archiveEntity, archive, totalFiles: 0,
            cancellationToken);
        return true;
    }

    private static async Task MarkArchiveWithNoContentAsync(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive, int totalFiles, CancellationToken cancellationToken)
    {
        archiveEntity.Status = ArchiveStatus.Error;
        archiveEntity.ErrorMessage = "No DAZ content directories were found in this archive.";
        archiveEntity.InstallCompletedAt = DateTime.Now;
        PublishArchiveProgress(archive, ArchiveStatus.Error, archiveEntity.ErrorMessage,
            errorMessage: archiveEntity.ErrorMessage, totalFiles: totalFiles, progressPercent: 100);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<bool> RejectDuplicateContentAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary, Archive archiveEntity, LoadedArchive archive, bool isResumedInstall,
        CancellationToken cancellationToken)
    {
        if (isResumedInstall)
            return false;

        var contentDuplicateExists = await dbContext.Archives.AnyAsync(x =>
                x.AssetLibraryId == assetLibrary.Id &&
                x.ParentArchiveId == null &&
                x.ContentFingerprint == archiveEntity.ContentFingerprint &&
                x.Status == ArchiveStatus.Installed &&
                x.Id != archiveEntity.Id,
            cancellationToken);

        if (!contentDuplicateExists)
            return false;

        await RemoveArchiveTreeAsync(dbContext, archiveEntity.Id, cancellationToken);
        PublishDuplicateArchiveProgress(archive, "An archive with identical content is already tracked for this library.");
        return true;
    }

    private bool ShouldCreateInstallBackup(bool isResumedInstall)
    {
        return !isResumedInstall && settingsService.CurrentSettings.CreateBackupBeforeInstall;
    }

    private async Task<Exception?> TryBackupArchiveAsync(AssetLibrary assetLibrary, string sourcePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await BackupArchiveAsync(assetLibrary, sourcePath, cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task TryRefreshArchiveThumbnailAsync(AssetLibrary assetLibrary, string sourceArchivePath,
        Archive archiveEntity, DazArchiveScanResult scanResult, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshArchiveThumbnailAsync(assetLibrary, sourceArchivePath, archiveEntity, scanResult,
                cancellationToken);
        }
        catch (Exception ex)
        {
            archiveEntity.ThumbnailFileExtension = null;
            Console.WriteLine("Failed to refresh archive thumbnail: {0}", ex);
        }
    }

    private static async Task BeginFileInstallAsync(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive, int installableEntryCount, CancellationToken cancellationToken)
    {
        archiveEntity.Status = ArchiveStatus.Installing;
        PublishArchiveProgress(archive, ArchiveStatus.Installing, "Installing files", processedFiles: 0,
            totalFiles: installableEntryCount, progressPercent: 0);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async IAsyncEnumerable<LoadedArchive> InstallEntriesAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary, LoadedArchive archive, IReadOnlyList<InstallableArchiveEntry> installableEntries,
        ProgressUpdateThrottler progressThrottler, InstallEntriesSummary summary, ProgressYieldState progressYieldState,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var persistImmediately = archiveInstallCoordinator.AllowsConcurrentPersistence;
        var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            for (var index = 0; index < installableEntries.Count; index++)
            {
                var entryResult = await InstallEntryWithProgressAsync(dbContext, assetLibrary, archive,
                    installableEntries, index, persistImmediately, cancellationToken);

                if (entryResult.Failure is not null)
                {
                    summary.Failure = entryResult.Failure;
                    yield break;
                }

                if (entryResult.InstallResult == InstallEntryResult.SkippedDuplicate)
                    summary.SkippedDuplicateCount++;

                if (progressThrottler.ShouldUpdate(force: index == installableEntries.Count - 1)
                    && await YieldArchiveProgressAsync(archive, progressYieldState))
                    yield return archive;
            }
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
        }
    }

    private async Task<InstallEntryProgressResult> InstallEntryWithProgressAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary, LoadedArchive archive, IReadOnlyList<InstallableArchiveEntry> installableEntries,
        int index, bool persistImmediately, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = installableEntries[index];
        PublishInstallingEntryProgress(archive, entry, index, installableEntries.Count);

        try
        {
            var installResult = await InstallEntryAsync(dbContext, assetLibrary, entry,
                    entry.PendingInstallRecordId, persistImmediately, cancellationToken);

            PublishInstalledEntryProgress(archive, entry, index, installableEntries.Count);
            return new InstallEntryProgressResult(installResult, null);
        }
        catch (Exception ex)
        {
            return new InstallEntryProgressResult(InstallEntryResult.Installed, ex);
        }
    }

    private static void PublishInstallingEntryProgress(LoadedArchive archive, InstallableArchiveEntry entry,
        int index, int totalFiles)
    {
        PublishArchiveProgress(archive, ArchiveStatus.Installing, $"Installing {entry.InstalledRelativePath}",
            currentFile: entry.InstalledRelativePath, processedFiles: index,
            totalFiles: totalFiles,
            progressPercent: totalFiles == 0 ? 0 : (double)index / totalFiles * 100d);
    }

    private static void PublishInstalledEntryProgress(LoadedArchive archive, InstallableArchiveEntry entry,
        int index, int totalFiles)
    {
        PublishArchiveProgress(archive, ArchiveStatus.Installing, $"Installing {entry.InstalledRelativePath}",
            currentFile: entry.InstalledRelativePath, processedFiles: index + 1,
            totalFiles: totalFiles,
            progressPercent: (double)(index + 1) / totalFiles * 100d);
    }

    private static async Task CompleteArchiveInstallAsync(ApplicationDbContext dbContext, AssetLibrary assetLibrary,
        Archive archiveEntity, LoadedArchive archive, int installableEntryCount, int skippedDuplicateCount,
        CancellationToken cancellationToken)
    {
        archiveEntity.Status = ArchiveStatus.Installed;
        archiveEntity.InstallCompletedAt = DateTime.Now;
        assetLibrary.LastUsed = DateTime.Now;

        var completionStatus = skippedDuplicateCount > 0
            ? $"Installation complete ({skippedDuplicateCount} duplicate file{(skippedDuplicateCount == 1 ? "" : "s")} skipped)"
            : "Installation complete";
        PublishArchiveProgress(archive, ArchiveStatus.Installed, completionStatus, currentFile: null,
            processedFiles: installableEntryCount, totalFiles: installableEntryCount, progressPercent: 100);

        await dbContext.SaveChangesAsync(cancellationToken);
        archive.MarkInstallCompleted();
    }

    private static void PublishDuplicateArchiveProgress(LoadedArchive archive, string errorMessage)
    {
        PublishArchiveProgress(archive, ArchiveStatus.Duplicate, "Archive already installed",
            errorMessage: errorMessage, progressPercent: 100);
    }

    private static void PublishArchiveProgress(
        LoadedArchive archive,
        ArchiveStatus status,
        string? statusText,
        string? errorMessage = null,
        string? currentFile = null,
        int processedFiles = 0,
        int totalFiles = 0,
        double progressPercent = 0)
    {
        archive.SetInstallProgressSilently(status, statusText, errorMessage, currentFile, processedFiles, totalFiles,
            progressPercent);
    }

    private static ValueTask<bool> YieldArchiveProgressAsync(LoadedArchive archive, ref long lastProgressYieldTicks,
        bool force = false)
    {
        if (!force)
        {
            var now = Environment.TickCount64;
            if (now - lastProgressYieldTicks < InstallProgressYieldInterval.TotalMilliseconds)
                return ValueTask.FromResult(false);

            lastProgressYieldTicks = now;
        }
        else
        {
            lastProgressYieldTicks = Environment.TickCount64;
        }

        return ValueTask.FromResult(true);
    }

    private static ValueTask<bool> YieldArchiveProgressAsync(LoadedArchive archive, ProgressYieldState state,
        bool force = false)
    {
        return YieldArchiveProgressAsync(archive, ref state.LastProgressYieldTicks, force);
    }

    private static async Task CollectInstallableEntriesAsync(ApplicationDbContext dbContext, IArchive sourceArchive,
        Archive archiveEntity,
        string workingDirectory, List<InstallableArchiveEntry> installableEntries, string libraryRoot,
        DazArchiveScanResult? scanNode, CancellationToken cancellationToken)
    {
        foreach (var entry in sourceArchive.Entries.Where(x => !x.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedEntryPath = NormalizeArchivePath(entry.Key ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedEntryPath))
                continue;

            if (DazArchiveScanner.ShouldIgnoreArchiveEntry(normalizedEntryPath))
                continue;

            if (DazArchiveScanner.TryGetInstalledRelativePath(normalizedEntryPath, out var installedRelativePath,
                    out var contentRoot))
            {
                archiveEntity.ContentRoot ??= contentRoot;
                installedRelativePath =
                    DazArchiveScanner.CanonicalizeInstalledRelativePath(installedRelativePath, libraryRoot);

                var extractedPath = Path.Combine(workingDirectory,
                    $"{Guid.NewGuid():N}{Path.GetExtension(normalizedEntryPath)}");
                await using (var sourceStream = await entry.OpenEntryStreamAsync(cancellationToken))
                await using (var outputStream = File.Create(extractedPath))
                {
                    await sourceStream.CopyToAsync(outputStream, cancellationToken);
                }

                var fileInfo = new FileInfo(extractedPath);
                var fileHash = await ArchiveContentFingerprint.HashFileAsync(extractedPath, cancellationToken);

                installableEntries.Add(new InstallableArchiveEntry
                {
                    Archive = archiveEntity,
                    ArchiveRelativePath = normalizedEntryPath,
                    InstalledRelativePath = installedRelativePath,
                    ExtractedFilePath = extractedPath,
                    FileSize = (ulong)fileInfo.Length,
                    FileHash = fileHash
                });
                continue;
            }

            if (!DazArchiveScanner.IsNestedArchive(normalizedEntryPath))
                continue;

            var nestedArchivePath = Path.Combine(workingDirectory,
                $"{Guid.NewGuid():N}{Path.GetExtension(normalizedEntryPath)}");
            await using (var sourceStream = await entry.OpenEntryStreamAsync(cancellationToken))
            await using (var outputStream = File.Create(nestedArchivePath))
            {
                await sourceStream.CopyToAsync(outputStream, cancellationToken);
            }

            var nestedInfo = new FileInfo(nestedArchivePath);
            var nestedScan = scanNode?.FindNestedScan(normalizedEntryPath);
            var nestedArchive = new Archive
            {
                ArchiveName = Path.GetFileName(normalizedEntryPath),
                ArchiveSize = (ulong)nestedInfo.Length,
                Status = ArchiveStatus.Loading,
                AssetLibraryId = archiveEntity.AssetLibraryId,
                ParentArchive = archiveEntity,
                InstallStartedAt = archiveEntity.InstallStartedAt
            };
            ApplyScanMetadataToArchive(nestedArchive, nestedScan);

            dbContext.Archives.Add(nestedArchive);
            await dbContext.SaveChangesAsync(cancellationToken);

            using var nestedSourceArchive = await Task
                .Run(() => ArchiveFactory.OpenArchive(nestedArchivePath), cancellationToken);
            await CollectInstallableEntriesAsync(dbContext, nestedSourceArchive, nestedArchive, workingDirectory,
                installableEntries, libraryRoot, nestedScan,
                cancellationToken);
        }
    }

    private static void ApplyScanMetadataToArchive(Archive archiveEntity, DazArchiveScanResult? scan)
    {
        if (scan is null)
            return;

        archiveEntity.DisplayName = scan.DisplayName;
        archiveEntity.ContentRoot = scan.ContentRoot;
        archiveEntity.AssetTypes = AssetTypeCollection.Serialize(scan.AssetTypes);
        archiveEntity.Categories = SerializeCategories(scan.Categories);
    }

    private async Task PrepopulateInstallManifestAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary, List<InstallableArchiveEntry> installableEntries,
        CancellationToken cancellationToken)
    {
        var libraryRoot = GetCanonicalPath(assetLibrary.Path);
        var manifestTimestamp = DateTime.Now;

        foreach (var entry in installableEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var installedRelativePath =
                DazArchiveScanner.CanonicalizeInstalledRelativePath(entry.InstalledRelativePath, libraryRoot);
            var installedDirectory = Path.GetDirectoryName(installedRelativePath) ?? string.Empty;
            var fileName = Path.GetFileName(installedRelativePath);
            var lockKey = DestinationPathLockRegistry.CreateLockKey(assetLibrary.Id, installedRelativePath);

            var installedFile = await destinationPathLockRegistry.ExecuteAsync(lockKey, () =>
                    FindOrCreateInstalledFileAsync(dbContext, assetLibrary.Id, installedDirectory, fileName,
                        cancellationToken),
                cancellationToken);

            var assetFile = new AssetFile
            {
                Archive = entry.Archive,
                ArchiveRelativePath = entry.ArchiveRelativePath,
                InstalledRelativePath = installedRelativePath,
                FileHash = entry.FileHash,
                FileSize = entry.FileSize
            };

            var installRecord = new InstallRecord
            {
                Archive = entry.Archive,
                InstalledFile = installedFile,
                AssetFile = assetFile,
                InstallRecordStatus = InstallRecordStatus.Pending,
                InstalledAt = manifestTimestamp
            };

            dbContext.AssetFiles.Add(assetFile);
            dbContext.InstallRecords.Add(installRecord);
            entry.PendingInstallRecordId = installRecord.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<PreparedArchiveEntityResult> PrepareArchiveEntityForInstallAsync(
        ApplicationDbContext dbContext, AssetLibrary assetLibrary, FileInfo sourceInfo,
        CancellationToken cancellationToken)
    {
        var resumableArchive = await FindResumableArchiveAsync(dbContext, assetLibrary.Id, sourceInfo.Name,
                (ulong)sourceInfo.Length, cancellationToken);

        if (resumableArchive is not null)
        {
            await PrepareExistingArchiveForReinstallAsync(dbContext, resumableArchive, cancellationToken);
            return new PreparedArchiveEntityResult(resumableArchive, IsDuplicate: false, IsResumed: true);
        }

        var duplicateExists = await dbContext.Archives.AnyAsync(x =>
                x.AssetLibraryId == assetLibrary.Id &&
                x.ParentArchiveId == null &&
                x.ArchiveName == sourceInfo.Name &&
                x.ArchiveSize == (ulong)sourceInfo.Length &&
                x.Status == ArchiveStatus.Installed,
            cancellationToken);

        if (duplicateExists)
            return new PreparedArchiveEntityResult(null, IsDuplicate: true, IsResumed: false);

        var archiveEntity = new Archive
        {
            ArchiveName = sourceInfo.Name,
            ArchiveSize = (ulong)sourceInfo.Length,
            Status = ArchiveStatus.Loading,
            AssetLibraryId = assetLibrary.Id,
            InstallStartedAt = DateTime.Now
        };

        dbContext.Archives.Add(archiveEntity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PreparedArchiveEntityResult(archiveEntity, IsDuplicate: false, IsResumed: false);
    }

    private static async Task<InstalledFile> FindOrCreateInstalledFileAsync(ApplicationDbContext dbContext,
        Guid assetLibraryId, string installedDirectory, string fileName, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var localMatch = dbContext.InstalledFiles.Local.FirstOrDefault(x =>
                x.AssetLibraryId == assetLibraryId &&
                x.InstalledPath == installedDirectory &&
                x.FileName == fileName);
            if (localMatch is not null)
                return localMatch;

            var installedFile = await dbContext.InstalledFiles
                .FirstOrDefaultAsync(x =>
                        x.AssetLibraryId == assetLibraryId &&
                        x.InstalledPath == installedDirectory &&
                        x.FileName == fileName,
                    cancellationToken);

            if (installedFile is not null)
                return installedFile;

            installedFile = new InstalledFile
            {
                AssetLibraryId = assetLibraryId,
                InstalledPath = installedDirectory,
                FileName = fileName
            };
            dbContext.InstalledFiles.Add(installedFile);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return installedFile;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                dbContext.Entry(installedFile).State = EntityState.Detached;
            }
        }

        return await dbContext.InstalledFiles
            .FirstAsync(x =>
                    x.AssetLibraryId == assetLibraryId &&
                    x.InstalledPath == installedDirectory &&
                    x.FileName == fileName,
                cancellationToken);
    }

    private async Task<InstallEntryResult> InstallEntryAsync(ApplicationDbContext dbContext, AssetLibrary assetLibrary,
        InstallableArchiveEntry entry, Guid pendingInstallRecordId, bool persistImmediately,
        CancellationToken cancellationToken)
    {
        var lockKey = DestinationPathLockRegistry.CreateLockKey(assetLibrary.Id, entry.InstalledRelativePath);
        return await destinationPathLockRegistry.ExecuteAsync(lockKey, async () =>
        {
            var libraryRoot = GetCanonicalPath(assetLibrary.Path);
            var installedRelativePath =
                DazArchiveScanner.CanonicalizeInstalledRelativePath(entry.InstalledRelativePath, libraryRoot);
            var destinationPath = Path.Combine(libraryRoot, installedRelativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);

            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            var installRecord = await dbContext.InstallRecords
                .Include(x => x.AssetFile)
                .Include(x => x.InstalledFile)
                .FirstAsync(x => x.Id == pendingInstallRecordId, cancellationToken);

            if (installRecord.InstallRecordStatus != InstallRecordStatus.Pending)
            {
                throw new InvalidOperationException(
                    $"Install record {pendingInstallRecordId} is not in the pending state.");
            }

            var installedFile = installRecord.InstalledFile;
            var assetFile = installRecord.AssetFile;

            var activeRecords = installedFile.Id == default
                ? []
                : await GetActiveInstallRecordInfosAsync(dbContext, installedFile.Id, cancellationToken);

            if (activeRecords.Any(x => string.Equals(x.FileHash, entry.FileHash, StringComparison.Ordinal)))
            {
                installRecord.InstallRecordStatus = InstallRecordStatus.Skipped;
                installRecord.InstalledAt = DateTime.Now;
                assetLibrary.LastUsed = DateTime.Now;

                await PersistInstallEntryChangesAsync(dbContext, null, persistImmediately, cancellationToken);
                return InstallEntryResult.SkippedDuplicate;
            }

            IReadOnlyList<Guid>? overrideRecordIds = activeRecords.Count > 0
                ? activeRecords.Select(x => x.Id).ToList()
                : null;

            var hash = await CopyWithHashAsync(entry.ExtractedFilePath, destinationPath, cancellationToken);

            assetFile.FileHash = hash;
            installRecord.InstallRecordStatus = activeRecords.Count == 0
                ? InstallRecordStatus.Installed
                : InstallRecordStatus.Replaced;
            installRecord.InstalledAt = DateTime.Now;
            assetLibrary.LastUsed = DateTime.Now;

            await PersistInstallEntryChangesAsync(dbContext, overrideRecordIds, persistImmediately, cancellationToken);
            return InstallEntryResult.Installed;
        }, cancellationToken);
    }

    private static async Task<List<ActiveInstallRecordInfo>> GetActiveInstallRecordInfosAsync(
        ApplicationDbContext dbContext, Guid installedFileId, CancellationToken cancellationToken)
    {
        return await dbContext.InstallRecords
            .Where(x => x.InstalledFileId == installedFileId
                        && !x.HasBeenOverriden
                        && (x.InstallRecordStatus == InstallRecordStatus.Installed
                            || x.InstallRecordStatus == InstallRecordStatus.Replaced
                            || x.InstallRecordStatus == InstallRecordStatus.Skipped))
            .Select(x => new ActiveInstallRecordInfo(x.Id, x.AssetFile.FileHash))
            .ToListAsync(cancellationToken);
    }

    private static async Task PersistInstallEntryChangesAsync(ApplicationDbContext dbContext,
        IReadOnlyList<Guid>? overrideRecordIds, bool persistImmediately, CancellationToken cancellationToken)
    {
        if (!persistImmediately)
        {
            if (overrideRecordIds is not { Count: > 0 })
                return;


            foreach (var recordId in overrideRecordIds)
            {
                var stub = new InstallRecord { Id = recordId };
                dbContext.InstallRecords.Attach(stub);
                stub.HasBeenOverriden = true;
            }

            return;
        }

        if (overrideRecordIds is { Count: > 0 })
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await dbContext.InstallRecords
                    .Where(x => overrideRecordIds.Contains(x.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.HasBeenOverriden, true), cancellationToken);
                dbContext.ChangeTracker.DetectChanges();
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        else
        {
            dbContext.ChangeTracker.DetectChanges();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        ClearInstallEntryTrackerState(dbContext);
    }

    private static void ClearInstallEntryTrackerState(ApplicationDbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AssetFile or InstallRecord or InstalledFile)
                entry.State = EntityState.Detached;
        }
    }

    private static async Task PrepareExistingArchiveForReinstallAsync(ApplicationDbContext dbContext,
        Archive rootArchive,
        CancellationToken cancellationToken)
    {
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, rootArchive.Id, cancellationToken);
        var installedFileIds = await dbContext.InstallRecords
            .Where(x => archiveIds.Contains(x.ArchiveId))
            .Select(x => x.InstalledFileId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var childArchives = await dbContext.Archives
            .Where(x => x.ParentArchiveId == rootArchive.Id)
            .ToListAsync(cancellationToken);
        dbContext.Archives.RemoveRange(childArchives);

        var rootAssetFiles = await dbContext.AssetFiles
            .Where(x => x.ArchiveId == rootArchive.Id)
            .ToListAsync(cancellationToken);
        dbContext.AssetFiles.RemoveRange(rootAssetFiles);

        rootArchive.DisplayName = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        var orphanedFiles = await dbContext.InstalledFiles
            .Include(x => x.InstallRecords)
            .Where(x => installedFileIds.Contains(x.Id) && !x.InstallRecords.Any())
            .ToListAsync(cancellationToken);
        dbContext.InstalledFiles.RemoveRange(orphanedFiles);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task BackupArchiveAsync(AssetLibrary assetLibrary, string sourcePath,
        CancellationToken cancellationToken)
    {
        var backupRoot = GetBackupRoot(assetLibrary);
        Directory.CreateDirectory(backupRoot);

        var destinationPath = Path.Combine(backupRoot, Path.GetFileName(sourcePath));

        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private async Task RefreshArchiveThumbnailAsync(AssetLibrary assetLibrary, string sourceArchivePath,
        Archive archiveEntity, DazArchiveScanResult scanResult, CancellationToken cancellationToken)
    {
        await DeleteArchiveThumbnailAsync(assetLibrary, archiveEntity, cancellationToken);
        archiveEntity.ThumbnailFileExtension = null;

        var thumbnailEntryPath = scanResult.ThumbnailArchiveRelativePath;
        if (string.IsNullOrWhiteSpace(thumbnailEntryPath))
            return;

        var thumbnailExtension = Path.GetExtension(thumbnailEntryPath);
        if (string.IsNullOrWhiteSpace(thumbnailExtension))
            return;

        using var sourceArchive = await Task
            .Run(() => ArchiveFactory.OpenArchive(sourceArchivePath), cancellationToken);
        var thumbnailEntry = sourceArchive.Entries.FirstOrDefault(x =>
            !x.IsDirectory && string.Equals(NormalizeArchivePath(x.Key ?? string.Empty), thumbnailEntryPath,
                StringComparison.OrdinalIgnoreCase));
        if (thumbnailEntry is null)
            return;

        var thumbnailRoot = GetThumbnailRoot(assetLibrary);
        Directory.CreateDirectory(thumbnailRoot);

        var destinationPath = Path.Combine(thumbnailRoot, $"{archiveEntity.Id}{thumbnailExtension}");
        await using var sourceStream =
            await thumbnailEntry.OpenEntryStreamAsync(cancellationToken);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);

        archiveEntity.ThumbnailFileExtension = thumbnailExtension;
    }

    private static async Task MarkArchiveAsFailedAsync(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive,
        Exception ex, CancellationToken cancellationToken)
    {
        archiveEntity.Status = ArchiveStatus.Error;
        archiveEntity.ErrorMessage = ex.Message;
        archiveEntity.InstallCompletedAt = DateTime.Now;

        PublishArchiveProgress(archive, ArchiveStatus.Error, ex.Message, errorMessage: ex.Message,
            progressPercent: 100);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string GetBackupRoot(AssetLibrary assetLibrary)
    {
        if (!string.IsNullOrWhiteSpace(assetLibrary.ArchiveBackupPath))
            return GetCanonicalPath(assetLibrary.ArchiveBackupPath);

        return GetCanonicalPath(!string.IsNullOrWhiteSpace(settingsService.CurrentSettings.DefaultArchiveBackupPath)
            ? settingsService.CurrentSettings.DefaultArchiveBackupPath
            : installerConfig.ArchiveBackupPath);
    }

    private string GetThumbnailRoot(AssetLibrary assetLibrary)
    {
        return !string.IsNullOrWhiteSpace(assetLibrary.ArchiveThumbnailPath)
            ? GetCanonicalPath(assetLibrary.ArchiveThumbnailPath)
            : GetCanonicalPath(!string.IsNullOrWhiteSpace(settingsService.CurrentSettings.DefaultArchiveThumbnailPath)
                ? settingsService.CurrentSettings.DefaultArchiveThumbnailPath
                : installerConfig.ArchiveThumbnailPath);
    }

    private async Task DeleteArchiveThumbnailAsync(AssetLibrary assetLibrary, Archive archiveEntity,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(archiveEntity.ThumbnailFileExtension))
            return;

        var thumbnailPath = Path.Combine(GetThumbnailRoot(assetLibrary),
            $"{archiveEntity.Id}{archiveEntity.ThumbnailFileExtension}");
        if (File.Exists(thumbnailPath))
            File.Delete(thumbnailPath);
    }

    private static async Task<HashSet<Guid>> GetArchiveTreeIdsAsync(ApplicationDbContext dbContext, Guid rootArchiveId,
        CancellationToken cancellationToken)
    {
        var pending = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        pending.Enqueue(rootArchiveId);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current))
                continue;

            var childIds = await dbContext.Archives.Where(x => x.ParentArchiveId == current).Select(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var childId in childIds)
                pending.Enqueue(childId);
        }

        return visited;
    }

    private static async Task<string> CopyWithHashAsync(string sourcePath, string destinationPath,
        CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var sourceStream = File.OpenRead(sourcePath);
        await using var destinationStream = File.Create(destinationPath);

        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
                break;

            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        sha256.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha256.Hash!);
    }

    private static string NormalizeArchivePath(string key)
    {
        return DazArchiveScanner.NormalizeArchivePath(key);
    }

    private static string SerializeCategories(IEnumerable<string> categories)
    {
        return string.Join(", ",
            categories
                .Select(DazArchiveScanner.NormalizeCategory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    private static void RemoveEmptyDirectories(string? directory, string stopAtDirectory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var current = new DirectoryInfo(directory);
        var stopAt = GetCanonicalPath(stopAtDirectory);

        while (current.Exists && !string.Equals(GetCanonicalPath(current.FullName), stopAt, StringComparison.Ordinal))
        {
            if (current.EnumerateFileSystemInfos().Any())
                break;

            var parent = current.Parent;
            current.Delete();

            if (parent is null)
                break;

            current = parent;
        }
    }

    private static string GetCanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrEmpty(root))
            return Path.TrimEndingDirectorySeparator(fullPath);

        var segments = fullPath[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var resolvedPath = root;

        foreach (var segment in segments)
        {
            var nextPath = Path.Combine(resolvedPath, segment);
            var isDirectory = Directory.Exists(nextPath);
            var isFile = !isDirectory && File.Exists(nextPath);

            if (!isDirectory && !isFile)
            {
                resolvedPath = nextPath;
                continue;
            }

            FileSystemInfo fileSystemInfo = isDirectory ? new DirectoryInfo(nextPath) : new FileInfo(nextPath);
            resolvedPath = fileSystemInfo.ResolveLinkTarget(true)?.FullName ?? fileSystemInfo.FullName;
        }

        return Path.TrimEndingDirectorySeparator(resolvedPath);
    }

    private readonly record struct ActiveInstallRecordInfo(Guid Id, string FileHash);

    private static void ApplyArchiveFingerprints(List<InstallableArchiveEntry> installableEntries, Archive rootArchive)
    {
        rootArchive.ContentFingerprint = ArchiveContentFingerprint.Compute(
            installableEntries.Select(x => (x.InstalledRelativePath, x.FileHash, x.FileSize)));

        foreach (var archiveGroup in installableEntries.GroupBy(x => x.Archive.Id))
        {
            if (archiveGroup.Key == rootArchive.Id)
                continue;

            var archiveEntity = archiveGroup.First().Archive;
            archiveEntity.ContentFingerprint = ArchiveContentFingerprint.Compute(
                archiveGroup.Select(x => (x.InstalledRelativePath, x.FileHash, x.FileSize)));
        }
    }

    private static async Task<Archive?> FindResumableArchiveAsync(ApplicationDbContext dbContext, Guid assetLibraryId,
        string archiveName, ulong archiveSize, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Archives
            .Where(x =>
                x.AssetLibraryId == assetLibraryId &&
                x.ParentArchiveId == null &&
                x.ArchiveName == archiveName &&
                x.ArchiveSize == archiveSize &&
                (x.Status == ArchiveStatus.Loading ||
                 x.Status == ArchiveStatus.Installing ||
                 x.Status == ArchiveStatus.Error))
            .ToListAsync(cancellationToken);

        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                $"Multiple resumable archives found for '{archiveName}' in library {assetLibraryId}.")
        };
    }

    private static async Task RemoveArchiveTreeAsync(ApplicationDbContext dbContext, Guid rootArchiveId,
        CancellationToken cancellationToken)
    {
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, rootArchiveId, cancellationToken);
        var archives = await dbContext.Archives.Where(x => archiveIds.Contains(x.Id)).ToListAsync(cancellationToken);
        dbContext.Archives.RemoveRange(archives);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private enum InstallEntryResult
    {
        Installed,
        SkippedDuplicate
    }

    private readonly record struct PreparedArchiveEntityResult(
        Archive? ArchiveEntity,
        bool IsDuplicate,
        bool IsResumed);

    private readonly record struct ArchiveScanAttempt(DazArchiveScanResult? Result, Exception? Failure);

    private readonly record struct InstallableEntryCollectionResult(
        List<InstallableArchiveEntry> Entries,
        Exception? Failure);

    private readonly record struct InstallEntryProgressResult(
        InstallEntryResult InstallResult,
        Exception? Failure);

    private sealed class InstallEntriesSummary
    {
        public int SkippedDuplicateCount { get; set; }
        public Exception? Failure { get; set; }
    }

    private sealed class ProgressYieldState
    {
        public long LastProgressYieldTicks;
    }

    private sealed class InstallableArchiveEntry
    {
        public required Archive Archive { get; init; }
        public required string ArchiveRelativePath { get; init; }
        public required string InstalledRelativePath { get; init; }
        public required string ExtractedFilePath { get; init; }
        public required ulong FileSize { get; init; }
        public required string FileHash { get; init; }
        public Guid PendingInstallRecordId { get; set; }
    }
}