using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.Services;

public class ArchiveOverrideService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InstallerConfig installerConfig,
    DestinationPathLockRegistry destinationPathLockRegistry)
    : IArchiveOverrideService
{
    public async Task<bool> HasOverridesAsync(Guid rootArchiveId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ArchiveOverrides
            .AnyAsync(x => x.RootArchiveId == rootArchiveId, cancellationToken);
    }

    public async Task<int> GetOverrideCountAsync(Guid rootArchiveId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ArchiveOverrides
            .CountAsync(x => x.RootArchiveId == rootArchiveId, cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveOverrideInfo>> GetOverridesAsync(
        Guid rootArchiveId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ArchiveOverrides
            .AsNoTracking()
            .Where(x => x.RootArchiveId == rootArchiveId)
            .OrderBy(x => x.InstalledRelativeDirectory)
            .ThenBy(x => x.FileName)
            .Select(x => new ArchiveOverrideInfo(
                x.Id,
                x.InstalledRelativeDirectory,
                x.FileName,
                x.Mode,
                x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCandidateDirectoriesAsync(
        Guid rootArchiveId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var archive = await dbContext.Archives
            .AsNoTracking()
            .Include(x => x.AssetLibrary)
            .FirstOrDefaultAsync(x => x.Id == rootArchiveId && x.ParentArchiveId == null, cancellationToken);

        if (archive is null)
            return [];

        var libraryRoot = GetCanonicalPath(archive.AssetLibrary.Path);
        var archiveIds = await GetArchiveTreeIdsAsync(dbContext, rootArchiveId, cancellationToken);
        var installedDirectories = await dbContext.InstallRecords
            .AsNoTracking()
            .Where(x => archiveIds.Contains(x.ArchiveId))
            .Select(x => x.InstalledFile.InstalledPath)
            .Distinct()
            .ToListAsync(cancellationToken);

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var canonicalDirectory in from installedDirectory in installedDirectories
                 where !string.IsNullOrWhiteSpace(installedDirectory)
                 select DazArchiveScanner.CanonicalizeInstalledRelativePath(
                     NormalizeRelativePath(installedDirectory),
                     libraryRoot))
        {
            AddDirectoryAndParents(directories, canonicalDirectory);
        }

        return directories
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task AddOverridesAsync(
        Guid rootArchiveId,
        string installedRelativeDirectory,
        IReadOnlyList<string> sourceFilePaths,
        CancellationToken cancellationToken = default)
    {
        if (sourceFilePaths.Count == 0)
            return;

        ValidateRelativeDirectory(installedRelativeDirectory);
        var normalizedDirectory = NormalizeRelativePath(installedRelativeDirectory);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var archive = await dbContext.Archives
            .Include(x => x.AssetLibrary)
            .FirstOrDefaultAsync(x => x.Id == rootArchiveId && x.ParentArchiveId == null, cancellationToken);

        if (archive is null)
            throw new InvalidOperationException("Archive not found.");

        if (archive.Status != ArchiveStatus.Installed)
            throw new InvalidOperationException("Overrides can only be added to installed archives.");

        var libraryRoot = GetCanonicalPath(archive.AssetLibrary.Path);

        foreach (var sourceFilePath in sourceFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
                throw new FileNotFoundException("Override source file was not found.", sourceFilePath);

            var fileName = ValidateFileName(Path.GetFileName(sourceFilePath));
            var (storedDirectory, storedFileName, installedRelativePath) = ResolveOverridePaths(
                libraryRoot,
                normalizedDirectory,
                fileName);
            var lockKey = DestinationPathLockRegistry.CreateLockKey(archive.AssetLibraryId, installedRelativePath);

            await destinationPathLockRegistry.ExecuteAsync(lockKey, async () =>
            {
                var destinationPath = Path.Combine(libraryRoot, installedRelativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                var existingOverride = await dbContext.ArchiveOverrides
                    .FirstOrDefaultAsync(x =>
                            x.RootArchiveId == rootArchiveId &&
                            x.InstalledRelativeDirectory == storedDirectory &&
                            x.FileName == storedFileName,
                        cancellationToken);

                var overrideId = existingOverride?.Id ?? Guid.NewGuid();
                var overrideStorageDirectory = GetOverrideStorageDirectory(rootArchiveId, overrideId);
                Directory.CreateDirectory(overrideStorageDirectory);

                if (existingOverride is not null)
                {
                    switch (existingOverride.Mode)
                    {
                        case ArchiveOverrideMode.Replacement when
                            !string.IsNullOrWhiteSpace(existingOverride.OriginalFileBackupPath) &&
                            File.Exists(existingOverride.OriginalFileBackupPath):
                            File.Copy(existingOverride.OriginalFileBackupPath, destinationPath, overwrite: true);
                            break;
                        case ArchiveOverrideMode.Addition when File.Exists(destinationPath):
                            File.Delete(destinationPath);
                            break;
                        default:
                            break;
                    }

                    DeleteManagedOverrideFiles(existingOverride);
                    dbContext.ArchiveOverrides.Remove(existingOverride);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                var managedFilePath = Path.Combine(overrideStorageDirectory, $"custom{Path.GetExtension(fileName)}");
                File.Copy(sourceFilePath, managedFilePath, overwrite: true);

                string? originalBackupPath = null;
                ArchiveOverrideMode mode;
                if (File.Exists(destinationPath))
                {
                    mode = ArchiveOverrideMode.Replacement;
                    originalBackupPath =
                        Path.Combine(overrideStorageDirectory, $"original{Path.GetExtension(fileName)}");
                    File.Copy(destinationPath, originalBackupPath, overwrite: true);
                }
                else
                {
                    mode = ArchiveOverrideMode.Addition;
                }

                File.Copy(sourceFilePath, destinationPath, overwrite: true);

                dbContext.ArchiveOverrides.Add(new ArchiveOverride
                {
                    Id = overrideId,
                    RootArchiveId = rootArchiveId,
                    AssetLibraryId = archive.AssetLibraryId,
                    InstalledRelativeDirectory = storedDirectory,
                    FileName = storedFileName,
                    ManagedFilePath = managedFilePath,
                    OriginalFileBackupPath = originalBackupPath,
                    Mode = mode,
                    CreatedAt = DateTime.Now
                });

                await dbContext.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
        }
    }

    public async Task DeleteOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var archiveOverride = await dbContext.ArchiveOverrides
            .Include(x => x.AssetLibrary)
            .FirstOrDefaultAsync(x => x.Id == overrideId, cancellationToken);

        if (archiveOverride is null)
            return;

        var libraryRoot = GetCanonicalPath(archiveOverride.AssetLibrary.Path);
        var installedRelativePath = DazArchiveScanner.CanonicalizeInstalledRelativePath(
            ResolveInstalledRelativePath(
                archiveOverride.InstalledRelativeDirectory,
                archiveOverride.FileName),
            libraryRoot);
        var lockKey = DestinationPathLockRegistry.CreateLockKey(
            archiveOverride.AssetLibraryId,
            installedRelativePath);

        await destinationPathLockRegistry.ExecuteAsync(lockKey, async () =>
        {
            var destinationPath = Path.Combine(libraryRoot, installedRelativePath);

            if (archiveOverride.Mode == ArchiveOverrideMode.Replacement)
            {
                if (string.IsNullOrWhiteSpace(archiveOverride.OriginalFileBackupPath) ||
                    !File.Exists(archiveOverride.OriginalFileBackupPath))
                {
                    throw new InvalidOperationException(
                        "Cannot restore the original file because the override backup is missing.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(archiveOverride.OriginalFileBackupPath, destinationPath, overwrite: true);
            }
            else if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
                RemoveEmptyDirectories(Path.GetDirectoryName(destinationPath), libraryRoot);
            }

            DeleteManagedOverrideFiles(archiveOverride);
            dbContext.ArchiveOverrides.Remove(archiveOverride);
            await dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private static (string StoredDirectory, string StoredFileName, string InstalledRelativePath) ResolveOverridePaths(
        string libraryRoot,
        string installedRelativeDirectory,
        string fileName)
    {
        var installedRelativePath = ResolveInstalledRelativePath(installedRelativeDirectory, fileName);
        installedRelativePath = DazArchiveScanner.CanonicalizeInstalledRelativePath(installedRelativePath, libraryRoot);
        var storedDirectory = Path.GetDirectoryName(installedRelativePath) ?? string.Empty;
        var storedFileName = Path.GetFileName(installedRelativePath);
        return (storedDirectory, storedFileName, installedRelativePath);
    }

    private static string ResolveInstalledRelativePath(string installedRelativeDirectory, string fileName) =>
        string.IsNullOrEmpty(installedRelativeDirectory)
            ? fileName
            : Path.Combine(installedRelativeDirectory, fileName);

    private static void AddDirectoryAndParents(ISet<string> directories, string directory)
    {
        var normalized = NormalizeRelativePath(directory);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var segments = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var index = 1; index <= segments.Length; index++)
        {
            directories.Add(string.Join(Path.DirectorySeparatorChar.ToString(), segments.Take(index)));
        }
    }

    private static async Task<HashSet<Guid>> GetArchiveTreeIdsAsync(
        ApplicationDbContext dbContext,
        Guid rootArchiveId,
        CancellationToken cancellationToken)
    {
        var parentById = await dbContext.Archives
            .AsNoTracking()
            .Where(x => x.ParentArchiveId != null || x.Id == rootArchiveId)
            .Select(x => new { x.Id, x.ParentArchiveId })
            .ToDictionaryAsync(x => x.Id, x => x.ParentArchiveId, cancellationToken);

        return ArchiveTreeResolver.GetTreeIds(parentById, rootArchiveId);
    }

    private string GetOverrideStorageDirectory(Guid rootArchiveId, Guid overrideId) =>
        Path.Combine(installerConfig.ArchiveOverridePath, rootArchiveId.ToString("N"), overrideId.ToString("N"));

    private static void DeleteManagedOverrideFiles(ArchiveOverride archiveOverride)
    {
        if (File.Exists(archiveOverride.ManagedFilePath))
            File.Delete(archiveOverride.ManagedFilePath);

        if (!string.IsNullOrWhiteSpace(archiveOverride.OriginalFileBackupPath) &&
            File.Exists(archiveOverride.OriginalFileBackupPath))
        {
            File.Delete(archiveOverride.OriginalFileBackupPath);
        }

        var storageDirectory = Path.GetDirectoryName(archiveOverride.ManagedFilePath);
        if (string.IsNullOrWhiteSpace(storageDirectory) || !Directory.Exists(storageDirectory))
            return;

        if (!Directory.EnumerateFileSystemEntries(storageDirectory).Any())
            Directory.Delete(storageDirectory);
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim();
        return normalized.Trim(Path.DirectorySeparatorChar);
    }

    private static void ValidateRelativeDirectory(string path)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        if (Path.IsPathRooted(path))
            throw new ArgumentException("Directory path must be relative.", nameof(path));

        if (path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Directory path cannot contain '.' or '..' segments.", nameof(path));
        }
    }

    private static string ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? throw new ArgumentException("File name contains invalid characters.", nameof(fileName))
            : fileName;
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

        var segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
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
}