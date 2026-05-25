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
    DestinationPathLockRegistry destinationPathLockRegistry)
    : IDazArchiveInstaller
{
    public async IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(Guid assetLibraryId,
        IEnumerable<LoadedArchive> archives,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var archiveList = archives.ToList();
        if (archiveList.Count == 0)
            yield break;

        var maxParallelism = Math.Max(1, installerConfig.MaxConcurrentArchiveInstalls);
        if (maxParallelism == 1 || archiveList.Count == 1)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var assetLibrary =
                await dbContext.AssetLibraries.FirstAsync(x => x.Id == assetLibraryId, cancellationToken);

            foreach (var archive in archiveList)
            {
                await foreach (var progress in InstallArchiveAsync(dbContext, assetLibrary, archive,
                                   cancellationToken: cancellationToken))
                    yield return progress;
            }

            yield break;
        }

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
                await Task.WhenAll(workerTasks);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var progress in channel.Reader.ReadAllAsync(cancellationToken))
            yield return progress;

        await completionTask;
    }

    public async Task UninstallArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, archiveId, cancellationToken);
        var installRecords = await dbContext.InstallRecords
            .Include(x => x.InstalledFile)
            .ThenInclude(x => x.AssetLibrary)
            .Where(x => archiveIds.Contains(x.ArchiveId)
                        && !x.HasBeenOverriden
                        && x.InstallRecordStatus != InstallRecordStatus.Uninstalled)
            .ToListAsync(cancellationToken);

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
                    x.InstallRecordStatus != InstallRecordStatus.Uninstalled,
                cancellationToken);

            if (hasRemainingActiveOwners)
                continue;

            var libraryRoot = GetCanonicalPath(record.InstalledFile.AssetLibrary.Path);
            var fullPath = Path.Combine(libraryRoot, record.InstalledFile.InstalledPath, record.InstalledFile.FileName);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                RemoveEmptyDirectories(Path.GetDirectoryName(fullPath), libraryRoot);
            }
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        await foreach (var progress in InstallArchiveAsync(dbContext, archiveEntity.AssetLibrary, archive,
                           existingArchiveEntity: archiveEntity, cancellationToken: cancellationToken))
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

        var rootArchive = await dbContext.Archives.FirstOrDefaultAsync(x => x.Id == archiveId, cancellationToken);
        if (rootArchive is not null)
            dbContext.Archives.Remove(rootArchive);

        await dbContext.SaveChangesAsync(cancellationToken);

        var orphanedFiles = await dbContext.InstalledFiles
            .Include(x => x.InstallRecords)
            .Where(x => installedFileIds.Contains(x.Id) && !x.InstallRecords.Any())
            .ToListAsync(cancellationToken);

        dbContext.InstalledFiles.RemoveRange(orphanedFiles);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RunArchiveInstallWorkerAsync(Guid assetLibraryId, LoadedArchive archive, SemaphoreSlim semaphore,
        ChannelWriter<LoadedArchive> writer, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var assetLibrary =
                await dbContext.AssetLibraries.FirstAsync(x => x.Id == assetLibraryId, cancellationToken);

            await foreach (var progress in InstallArchiveAsync(dbContext, assetLibrary, archive,
                               cancellationToken: cancellationToken))
                await writer.WriteAsync(progress, cancellationToken);
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
        archive.ArchiveStatus = ArchiveStatus.Loading;
        archive.StatusText = "Preparing archive";
        archive.ErrorMessage = null;
        archive.ProgressPercent = 0;
        yield return archive;

        var sourceInfo = new FileInfo(archive.ArchivePath);
        if (!sourceInfo.Exists)
        {
            archive.ArchiveStatus = ArchiveStatus.Error;
            archive.ErrorMessage = "Archive file not found.";
            archive.StatusText = archive.ErrorMessage;
            yield return archive;
            yield break;
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
                    x.Status != ArchiveStatus.Uninstalled,
                cancellationToken);

            if (duplicateExists)
            {
                archive.ArchiveStatus = ArchiveStatus.Duplicate;
                archive.StatusText = "Archive already installed";
                archive.ErrorMessage = "An archive with the same name and size is already tracked for this library.";
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

        await dbContext.SaveChangesAsync(cancellationToken);
        archive.ArchiveId = archiveEntity.Id;

        var scanResult = archive.CachedScanResult;
        Exception? scanFailure = null;
        if (scanResult is null)
        {
            archive.StatusText = "Scanning archive";
            yield return archive;

            try
            {
                scanResult = await archiveScanner.ScanArchiveAsync(sourceInfo.FullName, cancellationToken);
            }
            catch (Exception ex)
            {
                scanFailure = ex;
            }
        }

        if (scanFailure is not null)
        {
            await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, scanFailure, cancellationToken);
            yield return archive;
            yield break;
        }

        if (scanResult is null)
            throw new InvalidOperationException("Archive scan did not return a result.");

        archiveEntity.ContentRoot = scanResult.ContentRoot;
        archiveEntity.AssetTypes = AssetTypeCollection.Serialize(scanResult.AssetTypes);
        archiveEntity.Categories = SerializeCategories(scanResult.Categories);
        archive.TotalFiles = scanResult.InstallableFileCount;
        archive.InstallableSizeBytes = scanResult.InstallableSize;
        archive.ContentRoot = scanResult.ContentRoot;
        archive.AssetTypes.Clear();
        foreach (var assetType in scanResult.AssetTypes)
            archive.AssetTypes.Add(assetType);
        archive.StatusText = "Scan complete";
        await dbContext.SaveChangesAsync(cancellationToken);
        yield return archive;

        if (scanResult.InstallableFileCount == 0)
        {
            archiveEntity.Status = ArchiveStatus.Error;
            archiveEntity.ErrorMessage = "No DAZ content directories were found in this archive.";
            archiveEntity.InstallCompletedAt = DateTime.Now;
            archive.ArchiveStatus = ArchiveStatus.Error;
            archive.ErrorMessage = archiveEntity.ErrorMessage;
            archive.StatusText = archiveEntity.ErrorMessage;
            await dbContext.SaveChangesAsync(cancellationToken);
            yield return archive;
            yield break;
        }

        using var workingDirectory = directoryService.GetTempDirectory();
        var installableEntries = new List<InstallableArchiveEntry>();

        Exception? scanError = null;
        try
        {
            using var sourceArchive = ArchiveFactory.OpenArchive(sourceInfo.FullName);
            await CollectInstallableEntriesAsync(dbContext, sourceArchive, archiveEntity,
                workingDirectory.DirectoryInfo.FullName,
                installableEntries, cancellationToken);
        }
        catch (Exception ex)
        {
            scanError = ex;
        }

        if (scanError is not null)
        {
            await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, scanError, cancellationToken);
            yield return archive;
            yield break;
        }

        archive.TotalFiles = installableEntries.Count;
        archive.ProcessedFiles = 0;

        if (installableEntries.Count == 0)
        {
            archiveEntity.Status = ArchiveStatus.Error;
            archiveEntity.ErrorMessage = "No DAZ content directories were found in this archive.";
            archiveEntity.InstallCompletedAt = DateTime.Now;
            archive.ArchiveStatus = ArchiveStatus.Error;
            archive.ErrorMessage = archiveEntity.ErrorMessage;
            archive.StatusText = archiveEntity.ErrorMessage;
            await dbContext.SaveChangesAsync(cancellationToken);
            yield return archive;
            yield break;
        }

        ApplyArchiveFingerprints(installableEntries, archiveEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (existingArchiveEntity is null)
        {
            var contentDuplicateExists = await dbContext.Archives.AnyAsync(x =>
                    x.AssetLibraryId == assetLibrary.Id &&
                    x.ParentArchiveId == null &&
                    x.ContentFingerprint == archiveEntity.ContentFingerprint &&
                    x.Status != ArchiveStatus.Uninstalled &&
                    x.Id != archiveEntity.Id,
                cancellationToken);

            if (contentDuplicateExists)
            {
                await RemoveArchiveTreeAsync(dbContext, archiveEntity.Id, cancellationToken);
                archive.ArchiveStatus = ArchiveStatus.Duplicate;
                archive.StatusText = "Archive already installed";
                archive.ErrorMessage = "An archive with identical content is already tracked for this library.";
                yield return archive;
                yield break;
            }
        }

        if (existingArchiveEntity is null && settingsService.CurrentSettings.CreateBackupBeforeInstall)
        {
            archive.StatusText = "Creating archive backup";
            yield return archive;

            Exception? backupError = null;
            try
            {
                await BackupArchiveAsync(assetLibrary, sourceInfo.FullName, cancellationToken);
            }
            catch (Exception ex)
            {
                backupError = ex;
            }

            if (backupError is not null)
            {
                await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, backupError, cancellationToken);
                yield return archive;
                yield break;
            }
        }

        archiveEntity.Status = ArchiveStatus.Installing;
        archive.ArchiveStatus = ArchiveStatus.Installing;
        archive.StatusText = "Installing files";
        await dbContext.SaveChangesAsync(cancellationToken);
        yield return archive;

        var skippedDuplicateCount = 0;
        for (var index = 0; index < installableEntries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = installableEntries[index];
            archive.CurrentFile = entry.InstalledRelativePath;
            archive.ProcessedFiles = index;
            archive.ProgressPercent = installableEntries.Count == 0
                ? 0
                : (double)index / installableEntries.Count * 100d;
            archive.StatusText = $"Installing {entry.InstalledRelativePath}";

            Exception? installError = null;
            InstallEntryResult installResult = InstallEntryResult.Installed;
            try
            {
                installResult = await InstallEntryAsync(dbContext, assetLibrary, entry, cancellationToken);
            }
            catch (Exception ex)
            {
                installError = ex;
            }

            if (installError is not null)
            {
                await MarkArchiveAsFailedAsync(dbContext, archiveEntity, archive, installError, cancellationToken);
                yield return archive;
                yield break;
            }

            if (installResult == InstallEntryResult.SkippedDuplicate)
                skippedDuplicateCount++;

            archive.ProcessedFiles = index + 1;
            archive.ProgressPercent = (double)(index + 1) / installableEntries.Count * 100d;
            yield return archive;
        }

        archiveEntity.Status = ArchiveStatus.Installed;
        archiveEntity.InstallCompletedAt = DateTime.Now;
        assetLibrary.LastUsed = DateTime.Now;

        archive.ArchiveStatus = ArchiveStatus.Installed;
        archive.StatusText = skippedDuplicateCount > 0
            ? $"Installation complete ({skippedDuplicateCount} duplicate file{(skippedDuplicateCount == 1 ? "" : "s")} skipped)"
            : "Installation complete";
        archive.CurrentFile = null;
        archive.ProgressPercent = 100;

        await dbContext.SaveChangesAsync(cancellationToken);
        yield return archive;
    }

    private static async Task CollectInstallableEntriesAsync(ApplicationDbContext dbContext, IArchive sourceArchive,
        Archive archiveEntity,
        string workingDirectory, List<InstallableArchiveEntry> installableEntries, CancellationToken cancellationToken)
    {
        foreach (var entry in sourceArchive.Entries.Where(x => !x.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedEntryPath = NormalizeArchivePath(entry.Key ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedEntryPath))
                continue;

            if (DazArchiveScanner.ShouldIgnoreArchiveEntry(normalizedEntryPath))
                continue;

            if (TryGetInstalledRelativePath(normalizedEntryPath, out var installedRelativePath, out var contentRoot))
            {
                archiveEntity.ContentRoot ??= contentRoot;

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
            var nestedArchive = new Archive
            {
                ArchiveName = Path.GetFileName(normalizedEntryPath),
                ArchiveSize = (ulong)nestedInfo.Length,
                Status = ArchiveStatus.Loading,
                AssetLibraryId = archiveEntity.AssetLibraryId,
                ParentArchive = archiveEntity,
                InstallStartedAt = archiveEntity.InstallStartedAt
            };

            dbContext.Archives.Add(nestedArchive);
            await dbContext.SaveChangesAsync(cancellationToken);

            using var nestedSourceArchive = ArchiveFactory.OpenArchive(nestedArchivePath);
            await CollectInstallableEntriesAsync(dbContext, nestedSourceArchive, nestedArchive, workingDirectory,
                installableEntries,
                cancellationToken);
        }
    }

    private async Task<InstallEntryResult> InstallEntryAsync(ApplicationDbContext dbContext, AssetLibrary assetLibrary,
        InstallableArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        var lockKey = DestinationPathLockRegistry.CreateLockKey(assetLibrary.Id, entry.InstalledRelativePath);
        return await destinationPathLockRegistry.ExecuteAsync(lockKey, async () =>
        {
            var libraryRoot = GetCanonicalPath(assetLibrary.Path);
            var destinationPath = Path.Combine(libraryRoot, entry.InstalledRelativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);

            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            var installedDirectory = Path.GetDirectoryName(entry.InstalledRelativePath) ?? string.Empty;
            var fileName = Path.GetFileName(entry.InstalledRelativePath);

            var installedFile = await dbContext.InstalledFiles
                .Include(x => x.InstallRecords)
                .ThenInclude(x => x.AssetFile)
                .FirstOrDefaultAsync(x =>
                        x.AssetLibraryId == assetLibrary.Id &&
                        x.InstalledPath == installedDirectory &&
                        x.FileName == fileName,
                    cancellationToken);

            if (installedFile is null)
            {
                installedFile = new InstalledFile
                {
                    AssetLibraryId = assetLibrary.Id,
                    InstalledPath = installedDirectory,
                    FileName = fileName
                };
                dbContext.InstalledFiles.Add(installedFile);
            }

            var activeRecords = GetActiveInstallRecords(installedFile.InstallRecords).ToList();
            var hasMatchingOwner = activeRecords.Any(x =>
                string.Equals(x.AssetFile.FileHash, entry.FileHash, StringComparison.Ordinal));

            if (hasMatchingOwner)
            {
                var assetFile = new AssetFile
                {
                    Archive = entry.Archive,
                    ArchiveRelativePath = entry.ArchiveRelativePath,
                    InstalledRelativePath = entry.InstalledRelativePath,
                    FileHash = entry.FileHash,
                    FileSize = entry.FileSize
                };

                var installRecord = new InstallRecord
                {
                    Archive = entry.Archive,
                    InstalledFile = installedFile,
                    AssetFile = assetFile,
                    InstallRecordStatus = InstallRecordStatus.Skipped,
                    InstalledAt = DateTime.Now
                };

                dbContext.AssetFiles.Add(assetFile);
                dbContext.InstallRecords.Add(installRecord);
                assetLibrary.LastUsed = DateTime.Now;

                await dbContext.SaveChangesAsync(cancellationToken);
                return InstallEntryResult.SkippedDuplicate;
            }

            foreach (var activeRecord in activeRecords)
                activeRecord.HasBeenOverriden = true;

            var hash = await CopyWithHashAsync(entry.ExtractedFilePath, destinationPath, cancellationToken);

            var installedAssetFile = new AssetFile
            {
                Archive = entry.Archive,
                ArchiveRelativePath = entry.ArchiveRelativePath,
                InstalledRelativePath = entry.InstalledRelativePath,
                FileHash = hash,
                FileSize = entry.FileSize
            };

            var installedRecord = new InstallRecord
            {
                Archive = entry.Archive,
                InstalledFile = installedFile,
                AssetFile = installedAssetFile,
                InstallRecordStatus = activeRecords.Count == 0
                    ? InstallRecordStatus.Installed
                    : InstallRecordStatus.Replaced,
                InstalledAt = DateTime.Now
            };

            dbContext.AssetFiles.Add(installedAssetFile);
            dbContext.InstallRecords.Add(installedRecord);
            assetLibrary.LastUsed = DateTime.Now;

            await dbContext.SaveChangesAsync(cancellationToken);
            return InstallEntryResult.Installed;
        }, cancellationToken);
    }

    private static async Task PrepareExistingArchiveForReinstallAsync(ApplicationDbContext dbContext, Archive rootArchive,
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

    private static async Task MarkArchiveAsFailedAsync(ApplicationDbContext dbContext, Archive archiveEntity,
        LoadedArchive archive,
        Exception ex, CancellationToken cancellationToken)
    {
        archiveEntity.Status = ArchiveStatus.Error;
        archiveEntity.ErrorMessage = ex.Message;
        archiveEntity.InstallCompletedAt = DateTime.Now;

        archive.ArchiveStatus = ArchiveStatus.Error;
        archive.ErrorMessage = ex.Message;
        archive.StatusText = ex.Message;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string GetBackupRoot(AssetLibrary assetLibrary)
    {
        if (!string.IsNullOrWhiteSpace(assetLibrary.ArchiveBackupPath))
            return GetCanonicalPath(assetLibrary.ArchiveBackupPath);

        if (!string.IsNullOrWhiteSpace(settingsService.CurrentSettings.DefaultArchiveBackupPath))
            return GetCanonicalPath(settingsService.CurrentSettings.DefaultArchiveBackupPath);

        return GetCanonicalPath(installerConfig.ArchiveBackupPath);
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

    private static bool TryGetInstalledRelativePath(string archiveRelativePath, out string installedRelativePath,
        out string contentRoot)
    {
        var segments = archiveRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (!DazArchiveScanner.AssetRootDirectories.Contains(segments[index]))
                continue;

            installedRelativePath = Path.Combine(segments[index..]);
            contentRoot = index == 0 ? string.Empty : string.Join('/', segments[..index]);
            return true;
        }

        installedRelativePath = string.Empty;
        contentRoot = string.Empty;
        return false;
    }

    private static string NormalizeArchivePath(string key)
    {
        return DazArchiveScanner.NormalizeArchivePath(key);
    }

    private static string SerializeCategories(IEnumerable<string> categories)
    {
        return string.Join(", ", categories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
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

    private static IEnumerable<InstallRecord> GetActiveInstallRecords(IEnumerable<InstallRecord> records)
    {
        return records.Where(x => !x.HasBeenOverriden && x.InstallRecordStatus != InstallRecordStatus.Uninstalled);
    }

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
    }
}