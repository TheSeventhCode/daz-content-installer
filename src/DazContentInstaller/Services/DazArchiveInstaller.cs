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
    IArchiveOverrideService archiveOverrideService)
    : IDazArchiveInstaller
{
    private static readonly TimeSpan InstallProgressYieldInterval = TimeSpan.FromMilliseconds(100);

    public async IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(Guid assetLibraryId,
        IEnumerable<LoadedArchive> archives,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var archiveList = archives.ToList();
        if (archiveList.Count == 0)
            yield break;

        await foreach (var progress in RunInstallWorkersAsync(assetLibraryId, archiveList, cancellationToken))
            yield return progress;
    }

    public async Task UninstallArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default)
    {
        if (await archiveOverrideService.HasOverridesAsync(archiveId, cancellationToken).ConfigureAwait(false))
        {
            var overrideCount = await archiveOverrideService
                .GetOverrideCountAsync(archiveId, cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Cannot uninstall this archive while {overrideCount} override(s) remain. Remove overrides first.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, archiveId, cancellationToken).ConfigureAwait(false);
        var installRecords = await dbContext.InstallRecords
            .Include(x => x.InstalledFile)
            .ThenInclude(x => x.AssetLibrary)
            .Where(x => archiveIds.Contains(x.ArchiveId)
                        && !x.HasBeenOverriden
                        && x.InstallRecordStatus != InstallRecordStatus.Uninstalled)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var filesToDelete = new List<(string FullPath, string LibraryRoot)>();

        foreach (var record in installRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            record.InstallRecordStatus = InstallRecordStatus.Uninstalled;
            record.HasBeenOverriden = true;
            record.InstalledFile.AssetLibrary.LastUsed = DateTime.Now;

            var hasRemainingActiveOwners = await dbContext.InstallRecords.AnyAsync(x =>
                    x.InstalledFileId == record.InstalledFileId &&
                    x.Id != record.Id &&
                    !x.HasBeenOverriden &&
                    (x.InstallRecordStatus == InstallRecordStatus.Installed ||
                     x.InstallRecordStatus == InstallRecordStatus.Replaced ||
                     x.InstallRecordStatus == InstallRecordStatus.Skipped),
                cancellationToken).ConfigureAwait(false);

            if (hasRemainingActiveOwners)
                continue;

            var libraryRoot = GetCanonicalPath(record.InstalledFile.AssetLibrary.Path);
            var fullPath = Path.Combine(libraryRoot, record.InstalledFile.InstalledPath, record.InstalledFile.FileName);

            if (File.Exists(fullPath))
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
            }, cancellationToken).ConfigureAwait(false);
        }

        var archives = await dbContext.Archives.Where(x => archiveIds.Contains(x.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var archive in archives)
        {
            archive.Status = ArchiveStatus.Uninstalled;
            archive.InstallCompletedAt = DateTime.Now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<LoadedArchive> ReinstallArchiveAsync(Guid archiveId, LoadedArchive archive,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var archiveEntity = await dbContext.Archives
            .Include(x => x.AssetLibrary)
            .FirstOrDefaultAsync(x => x.Id == archiveId && x.ParentArchiveId == null, cancellationToken)
            .ConfigureAwait(false);

        if (archiveEntity is null)
            throw new InvalidOperationException("Archive not found.");

        if (archiveEntity.Status != ArchiveStatus.Uninstalled)
            throw new InvalidOperationException("Only uninstalled archives can be reinstalled.");

        var backupPath = Path.Combine(GetBackupRoot(archiveEntity.AssetLibrary), archiveEntity.ArchiveName);
        if (!File.Exists(backupPath))
            throw new InvalidOperationException("Archive backup file not found.");

        if (!string.Equals(archive.ArchivePath, backupPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Archive path does not match the backup file for this archive.");

        await PrepareExistingArchiveForReinstallAsync(dbContext, archiveEntity, cancellationToken).ConfigureAwait(false);

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
            await DeleteArchiveThumbnailAsync(rootArchive.AssetLibrary, rootArchive, cancellationToken).ConfigureAwait(false);

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
        var maxParallelism = Math.Max(1,
            Math.Min(settingsService.CurrentSettings.MaxConcurrentArchiveInstalls, archiveList.Count));
        var channel = Channel.CreateUnbounded<LoadedArchive>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

        var workerTasks = archiveList
            .Select(archive =>
                RunArchiveInstallWorkerAsync(assetLibraryId, archive, semaphore, channel.Writer, cancellationToken))
            .ToArray();

        var completionTask = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(workerTasks).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var progress in channel.Reader.ReadAllAsync(cancellationToken))
            yield return progress;

        await completionTask.ConfigureAwait(false);
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

        var workerTask = Task.Run(async () =>
        {
            try
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
                    .ConfigureAwait(false);
                var assetLibrary =
                    await dbContext.AssetLibraries.FirstAsync(x => x.Id == assetLibraryId, cancellationToken)
                        .ConfigureAwait(false);
                var existingArchiveEntity = await dbContext.Archives
                    .Include(x => x.AssetLibrary)
                    .FirstAsync(x => x.Id == existingArchiveId, cancellationToken)
                    .ConfigureAwait(false);

                await foreach (var progress in InstallArchiveAsync(dbContext, assetLibrary, archive,
                                   existingArchiveEntity, cancellationToken))
                    await channel.Writer.WriteAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var progress in channel.Reader.ReadAllAsync(cancellationToken))
            yield return progress;

        await workerTask.ConfigureAwait(false);
    }

    private async Task RunArchiveInstallWorkerAsync(Guid assetLibraryId, LoadedArchive archive, SemaphoreSlim semaphore,
        ChannelWriter<LoadedArchive> writer, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var assetLibrary =
                await dbContext.AssetLibraries.FirstAsync(x => x.Id == assetLibraryId, cancellationToken)
                    .ConfigureAwait(false);

            await foreach (var progress in InstallArchiveAsync(dbContext, assetLibrary, archive,
                               cancellationToken: cancellationToken))
                await writer.WriteAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async IAsyncEnumerable<LoadedArchive> InstallArchiveAsync(ApplicationDbContext dbContext,
        AssetLibrary assetLibrary,
        LoadedArchive archive, Archive? existingArchiveEntity = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        long lastProgressYieldTicks = 0;
        var progressThrottler = new ProgressUpdateThrottler(InstallProgressYieldInterval);

        archive.ResetInstallCompletion();
        PublishArchiveProgress(archive, ArchiveStatus.Loading, "Preparing archive", errorMessage: null,
            progressPercent: 0);
        if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
            yield return archive;

        var sourceInfo = new FileInfo(archive.ArchivePath);
        if (!sourceInfo.Exists)
        {
            PublishArchiveProgress(archive, ArchiveStatus.Error, "Archive file not found.",
                errorMessage: "Archive file not found.");
            if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                yield return archive;
            yield break;
        }

        if (existingArchiveEntity is null)
        {
            existingArchiveEntity = await FindResumableArchiveAsync(dbContext, assetLibrary.Id, sourceInfo.Name,
                    (ulong)sourceInfo.Length, cancellationToken)
                .ConfigureAwait(false);

            if (existingArchiveEntity is not null)
                await PrepareExistingArchiveForReinstallAsync(dbContext, existingArchiveEntity, cancellationToken)
                    .ConfigureAwait(false);
        }

        Archive archiveEntity;
        if (existingArchiveEntity is not null)
        {
            archiveEntity = existingArchiveEntity;
            archiveEntity.Status = ArchiveStatus.Loading;
            archiveEntity.ErrorMessage = null;
            archiveEntity.InstallStartedAt = DateTime.Now;
            archiveEntity.InstallCompletedAt = null;
        }
        else
        {
            var duplicateExists = await dbContext.Archives.AnyAsync(x =>
                    x.AssetLibraryId == assetLibrary.Id &&
                    x.ParentArchiveId == null &&
                    x.ArchiveName == sourceInfo.Name &&
                    x.ArchiveSize == (ulong)sourceInfo.Length &&
                    x.Status == ArchiveStatus.Installed,
                cancellationToken);

            if (duplicateExists)
            {
                PublishArchiveProgress(archive, ArchiveStatus.Duplicate, "Archive already installed",
                    errorMessage: "An archive with the same name and size is already tracked for this library.");
                if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                    yield return archive;
                yield break;
            }

            archiveEntity = new Archive
            {
                ArchiveName = sourceInfo.Name,
                ArchiveSize = (ulong)sourceInfo.Length,
                Status = ArchiveStatus.Loading,
                AssetLibraryId = assetLibrary.Id,
                InstallStartedAt = DateTime.Now
            };

            dbContext.Archives.Add(archiveEntity);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        archive.ArchiveId = archiveEntity.Id;

        var scanResult = existingArchiveEntity is not null ? null : archive.CachedScanResult;
        Exception? scanFailure = null;
        if (scanResult is null)
        {
            PublishArchiveProgress(archive, ArchiveStatus.Loading, "Scanning archive");
            if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                yield return archive;

            try
            {
                scanResult = await archiveScanner.ScanArchiveAsync(sourceInfo.FullName, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                scanFailure = ex;
            }
        }

        if (scanFailure is not null)
        {
            await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, scanFailure, cancellationToken)
                .ConfigureAwait(false);
            if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                yield return archive;
            yield break;
        }

        if (scanResult is null)
            throw new InvalidOperationException("Archive scan did not return a result.");

        archiveEntity.ContentRoot = scanResult.ContentRoot;
        archiveEntity.DisplayName = scanResult.ResolveRootDisplayName();
        archiveEntity.AssetTypes = AssetTypeCollection.Serialize(scanResult.AssetTypes);
        archiveEntity.Categories = SerializeCategories(scanResult.Categories);
        archive.InstallableSizeBytes = scanResult.InstallableSize;
        archive.ContentRoot = scanResult.ContentRoot;
        archive.ApplyScanMetadata(scanResult);
        archive.ReplaceClassifications(scanResult);
        PublishArchiveProgress(archive, ArchiveStatus.Loading, "Scan complete", totalFiles: scanResult.InstallableFileCount,
            progressPercent: 0);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
            yield return archive;

        if (scanResult.InstallableFileCount == 0)
        {
            archiveEntity.Status = ArchiveStatus.Error;
            archiveEntity.ErrorMessage = "No DAZ content directories were found in this archive.";
            archiveEntity.InstallCompletedAt = DateTime.Now;
            PublishArchiveProgress(archive, ArchiveStatus.Error, archiveEntity.ErrorMessage,
                errorMessage: archiveEntity.ErrorMessage);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                yield return archive;
            yield break;
        }

        using var workingDirectory = directoryService.GetTempDirectory();
        var installableEntries = new List<InstallableArchiveEntry>();
        var libraryRoot = GetCanonicalPath(assetLibrary.Path);

        Exception? scanError = null;
        try
        {
            using var sourceArchive = await Task
                .Run(() => ArchiveFactory.OpenArchive(sourceInfo.FullName), cancellationToken)
                .ConfigureAwait(false);
            await CollectInstallableEntriesAsync(dbContext, sourceArchive, archiveEntity,
                workingDirectory.DirectoryInfo.FullName,
                installableEntries, libraryRoot, scanResult, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            scanError = ex;
        }

        if (scanError is not null)
        {
            await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, scanError, cancellationToken)
                .ConfigureAwait(false);
            if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                yield return archive;
            yield break;
        }

        PublishArchiveProgress(archive, ArchiveStatus.Loading, "Collecting installable files", processedFiles: 0,
            totalFiles: installableEntries.Count);

        if (installableEntries.Count == 0)
        {
            archiveEntity.Status = ArchiveStatus.Error;
            archiveEntity.ErrorMessage = "No DAZ content directories were found in this archive.";
            archiveEntity.InstallCompletedAt = DateTime.Now;
            PublishArchiveProgress(archive, ArchiveStatus.Error, archiveEntity.ErrorMessage,
                errorMessage: archiveEntity.ErrorMessage, totalFiles: 0);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                yield return archive;
            yield break;
        }

        ApplyArchiveFingerprints(installableEntries, archiveEntity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (existingArchiveEntity is null)
        {
            var contentDuplicateExists = await dbContext.Archives.AnyAsync(x =>
                    x.AssetLibraryId == assetLibrary.Id &&
                    x.ParentArchiveId == null &&
                    x.ContentFingerprint == archiveEntity.ContentFingerprint &&
                    x.Status == ArchiveStatus.Installed &&
                    x.Id != archiveEntity.Id,
                cancellationToken).ConfigureAwait(false);

            if (contentDuplicateExists)
            {
                await RemoveArchiveTreeAsync(dbContext, archiveEntity.Id, cancellationToken).ConfigureAwait(false);
                PublishArchiveProgress(archive, ArchiveStatus.Duplicate, "Archive already installed",
                    errorMessage: "An archive with identical content is already tracked for this library.");
                if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                    yield return archive;
                yield break;
            }
        }

        if (existingArchiveEntity is null && settingsService.CurrentSettings.CreateBackupBeforeInstall)
        {
            PublishArchiveProgress(archive, ArchiveStatus.Loading, "Creating archive backup");
            if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                yield return archive;

            Exception? backupError = null;
            try
            {
                await BackupArchiveAsync(assetLibrary, sourceInfo.FullName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                backupError = ex;
            }

            if (backupError is not null)
            {
                await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, backupError, cancellationToken)
                    .ConfigureAwait(false);
                if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                    yield return archive;
                yield break;
            }
        }

        try
        {
            await RefreshArchiveThumbnailAsync(assetLibrary, sourceInfo.FullName, archiveEntity, scanResult,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            archiveEntity.ThumbnailFileExtension = null;
            Console.WriteLine("Failed to refresh archive thumbnail: {0}", ex);
        }

        await PrepopulateInstallManifestAsync(dbContext, assetLibrary, installableEntries, cancellationToken)
            .ConfigureAwait(false);

        archiveEntity.Status = ArchiveStatus.Installing;
        PublishArchiveProgress(archive, ArchiveStatus.Installing, "Installing files", processedFiles: 0,
            totalFiles: installableEntries.Count, progressPercent: 0);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
            yield return archive;

        var skippedDuplicateCount = 0;
        var persistImmediately = settingsService.CurrentSettings.MaxConcurrentArchiveInstalls > 1;
        var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (var index = 0; index < installableEntries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = installableEntries[index];
                PublishArchiveProgress(archive, ArchiveStatus.Installing, $"Installing {entry.InstalledRelativePath}",
                    currentFile: entry.InstalledRelativePath, processedFiles: index,
                    totalFiles: installableEntries.Count,
                    progressPercent: installableEntries.Count == 0
                        ? 0
                        : (double)index / installableEntries.Count * 100d);

                Exception? installError = null;
                InstallEntryResult installResult = InstallEntryResult.Installed;
                try
                {
                    installResult = await InstallEntryAsync(dbContext, assetLibrary, entry, entry.PendingInstallRecordId,
                            persistImmediately, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    installError = ex;
                }

                if (installError is not null)
                {
                    dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
                    await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, installError, cancellationToken)
                        .ConfigureAwait(false);
                    if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
                        yield return archive;
                    yield break;
                }

                if (installResult == InstallEntryResult.SkippedDuplicate)
                    skippedDuplicateCount++;

                PublishArchiveProgress(archive, ArchiveStatus.Installing, $"Installing {entry.InstalledRelativePath}",
                    currentFile: entry.InstalledRelativePath, processedFiles: index + 1,
                    totalFiles: installableEntries.Count,
                    progressPercent: (double)(index + 1) / installableEntries.Count * 100d);

                if (progressThrottler.ShouldUpdate(force: index == installableEntries.Count - 1)
                    && await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks))
                    yield return archive;
            }
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
        }

        archiveEntity.Status = ArchiveStatus.Installed;
        archiveEntity.InstallCompletedAt = DateTime.Now;
        assetLibrary.LastUsed = DateTime.Now;

        var completionStatus = skippedDuplicateCount > 0
            ? $"Installation complete ({skippedDuplicateCount} duplicate file{(skippedDuplicateCount == 1 ? "" : "s")} skipped)"
            : "Installation complete";
        PublishArchiveProgress(archive, ArchiveStatus.Installed, completionStatus, currentFile: null,
            processedFiles: installableEntries.Count, totalFiles: installableEntries.Count, progressPercent: 100);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        archive.MarkInstallCompleted();
        if (await YieldArchiveProgressAsync(archive, ref lastProgressYieldTicks, force: true))
            yield return archive;
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
                .Run(() => ArchiveFactory.OpenArchive(nestedArchivePath), cancellationToken)
                .ConfigureAwait(false);
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
                cancellationToken).ConfigureAwait(false);

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

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<InstalledFile> FindOrCreateInstalledFileAsync(ApplicationDbContext dbContext,
        Guid assetLibraryId, string installedDirectory, string fileName, CancellationToken cancellationToken)
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
                cancellationToken)
            .ConfigureAwait(false);

        if (installedFile is not null)
            return installedFile;

        installedFile = new InstalledFile
        {
            AssetLibraryId = assetLibraryId,
            InstalledPath = installedDirectory,
            FileName = fileName
        };
        dbContext.InstalledFiles.Add(installedFile);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return installedFile;
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
                .FirstAsync(x => x.Id == pendingInstallRecordId, cancellationToken)
                .ConfigureAwait(false);

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

            var overrideRecordIds = activeRecords.Count > 0
                ? activeRecords.Select(x => x.Id).ToList()
                : (IReadOnlyList<Guid>?)null;

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
            if (overrideRecordIds is { Count: > 0 })
            {
                foreach (var recordId in overrideRecordIds)
                {
                    var stub = new InstallRecord { Id = recordId };
                    dbContext.InstallRecords.Attach(stub);
                    stub.HasBeenOverriden = true;
                }
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
        await DeleteArchiveThumbnailAsync(assetLibrary, archiveEntity, cancellationToken).ConfigureAwait(false);
        archiveEntity.ThumbnailFileExtension = null;

        var thumbnailEntryPath = scanResult.ThumbnailArchiveRelativePath;
        if (string.IsNullOrWhiteSpace(thumbnailEntryPath))
            return;

        var thumbnailExtension = Path.GetExtension(thumbnailEntryPath);
        if (string.IsNullOrWhiteSpace(thumbnailExtension))
            return;

        using var sourceArchive = await Task
            .Run(() => ArchiveFactory.OpenArchive(sourceArchivePath), cancellationToken)
            .ConfigureAwait(false);
        var thumbnailEntry = sourceArchive.Entries.FirstOrDefault(x =>
            !x.IsDirectory && string.Equals(NormalizeArchivePath(x.Key ?? string.Empty), thumbnailEntryPath,
                StringComparison.OrdinalIgnoreCase));
        if (thumbnailEntry is null)
            return;

        var thumbnailRoot = GetThumbnailRoot(assetLibrary);
        Directory.CreateDirectory(thumbnailRoot);

        var destinationPath = Path.Combine(thumbnailRoot, $"{archiveEntity.Id}{thumbnailExtension}");
        await using var sourceStream = await thumbnailEntry.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);

        archiveEntity.ThumbnailFileExtension = thumbnailExtension;
    }

    private static async Task MarkArchiveAsFailedAsync(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive,
        Exception ex, CancellationToken cancellationToken)
    {
        archiveEntity.Status = ArchiveStatus.Error;
        archiveEntity.ErrorMessage = ex.Message;
        archiveEntity.InstallCompletedAt = DateTime.Now;

        PublishArchiveProgress(archive, ArchiveStatus.Error, ex.Message, errorMessage: ex.Message);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private string GetBackupRoot(AssetLibrary assetLibrary)
    {
        if (!string.IsNullOrWhiteSpace(assetLibrary.ArchiveBackupPath))
            return GetCanonicalPath(assetLibrary.ArchiveBackupPath);

        if (!string.IsNullOrWhiteSpace(settingsService.CurrentSettings.DefaultArchiveBackupPath))
            return GetCanonicalPath(settingsService.CurrentSettings.DefaultArchiveBackupPath);

        return GetCanonicalPath(installerConfig.ArchiveBackupPath);
    }

    private string GetThumbnailRoot(AssetLibrary assetLibrary)
    {
        if (!string.IsNullOrWhiteSpace(assetLibrary.ArchiveThumbnailPath))
            return GetCanonicalPath(assetLibrary.ArchiveThumbnailPath);

        if (!string.IsNullOrWhiteSpace(settingsService.CurrentSettings.DefaultArchiveThumbnailPath))
            return GetCanonicalPath(settingsService.CurrentSettings.DefaultArchiveThumbnailPath);

        return GetCanonicalPath(installerConfig.ArchiveThumbnailPath);
    }

    private async Task DeleteArchiveThumbnailAsync(AssetLibrary assetLibrary, Archive archiveEntity,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(archiveEntity.ThumbnailFileExtension))
            return;

        var thumbnailPath = Path.Combine(GetThumbnailRoot(assetLibrary), $"{archiveEntity.Id}{archiveEntity.ThumbnailFileExtension}");
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
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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