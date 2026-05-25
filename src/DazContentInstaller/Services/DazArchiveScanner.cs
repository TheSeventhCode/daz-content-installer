using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using SharpCompress.Archives;

namespace DazContentInstaller.Services;

public interface IDazArchiveScanner
{
    Task<DazArchiveScanResult> ScanArchiveAsync(string archivePath, CancellationToken cancellationToken = default);
}

public class DazArchiveScanner(IDirectoryService directoryService) : IDazArchiveScanner
{
    public static readonly HashSet<string> AssetRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "data",
        "runtime",
        "people",
        "props",
        "environments",
        "scene",
        "scenes",
        "scripts",
        "shader presets",
        "presets",
        "lights",
        "light presets",
        "documentation",
        "readme",
        "templates",
        "aniBlocks"
    };

    private static readonly HashSet<string> NestedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip",
        ".7z",
        ".rar",
        ".tar",
        ".gz",
        ".tgz",
        ".bz2",
        ".xz"
    };

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db"
    };

    public static readonly Dictionary<string, AssetType> FolderToAssetType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["characters"] = AssetType.Character,
        ["anatomy"] = AssetType.Anatomy,
        ["clothing"] = AssetType.Clothing,
        ["wardrobe"] = AssetType.Clothing,
        ["hair"] = AssetType.Hair,
        ["props"] = AssetType.Props,
        ["vehicles"] = AssetType.Props,
        ["environments"] = AssetType.Environment,
        ["scenes"] = AssetType.Environment,
        ["poses"] = AssetType.Poses,
        ["expressions"] = AssetType.Poses,
        ["animations"] = AssetType.Animations,
        ["aniBlocks"] = AssetType.Animations,
        ["materials"] = AssetType.Materials,
        ["shaders"] = AssetType.Materials,
        ["morphs"] = AssetType.Morphs,
        ["lights"] = AssetType.Lights,
        ["light presets"] = AssetType.Lights,
        ["cameras"] = AssetType.Cameras,
        ["scripts"] = AssetType.Scripts,
        ["textures"] = AssetType.Textures
    };

    private static readonly Dictionary<string, string> FolderToCanonicalCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wardrobe"] = "clothing",
        ["vehicles"] = "props",
        ["scenes"] = "environments",
        ["expressions"] = "poses",
        ["aniBlocks"] = "animations",
        ["shaders"] = "materials",
        ["light presets"] = "lights",
        ["readme"] = "documentation"
    };

    private static readonly Dictionary<string, string> CanonicalSegmentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["data"] = "data",
        ["runtime"] = "Runtime",
        ["people"] = "People",
        ["props"] = "Props",
        ["environments"] = "Environments",
        ["scene"] = "Scene",
        ["scenes"] = "Scenes",
        ["scripts"] = "Scripts",
        ["shader presets"] = "Shader Presets",
        ["presets"] = "Presets",
        ["lights"] = "Lights",
        ["light presets"] = "Light Presets",
        ["documentation"] = "Documentation",
        ["readme"] = "Documentation",
        ["templates"] = "Templates",
        ["aniBlocks"] = "aniBlocks",
        ["characters"] = "Characters",
        ["anatomy"] = "Anatomy",
        ["clothing"] = "Clothing",
        ["wardrobe"] = "Wardrobe",
        ["hair"] = "Hair",
        ["vehicles"] = "Vehicles",
        ["poses"] = "Poses",
        ["expressions"] = "Expressions",
        ["animations"] = "Animations",
        ["materials"] = "Materials",
        ["shaders"] = "Shaders",
        ["morphs"] = "Morphs",
        ["cameras"] = "Cameras",
        ["textures"] = "Textures"
    };

    public static string NormalizeCategory(string category)
    {
        return FolderToCanonicalCategory.TryGetValue(category, out var canonical) ? canonical : category;
    }

    public async Task<DazArchiveScanResult> ScanArchiveAsync(string archivePath,
        CancellationToken cancellationToken = default)
    {
        using var workingDirectory = directoryService.GetTempDirectory();
        return await ScanArchiveFileAsync(archivePath, workingDirectory.DirectoryInfo.FullName, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<DazArchiveScanResult> ScanArchiveFileAsync(string archivePath, string workingDirectory,
        CancellationToken cancellationToken)
    {
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists)
            throw new FileNotFoundException("Archive file not found.", archivePath);

        using var sourceArchive = await Task
            .Run(() => ArchiveFactory.OpenArchive(archiveInfo.FullName), cancellationToken)
            .ConfigureAwait(false);
        return await ScanOpenedArchiveAsync(sourceArchive, archiveInfo.Name, archiveInfo.FullName,
            (ulong)archiveInfo.Length,
            workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DazArchiveScanResult> ScanOpenedArchiveAsync(IArchive sourceArchive, string archiveName,
        string archivePath, ulong archiveSize, string workingDirectory, CancellationToken cancellationToken)
    {
        var files = new List<DazArchiveScanEntry>();
        var nestedArchives = new List<DazArchiveScanResult>();
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assetTypes = new HashSet<AssetType>();
        string? contentRoot = null;
        string? displayName = null;

        foreach (var entry in sourceArchive.Entries.Where(x => !x.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedEntryPath = NormalizeArchivePath(entry.Key ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedEntryPath))
                continue;

            if (ShouldIgnoreArchiveEntry(normalizedEntryPath))
                continue;

            if (displayName is null && DazProductMetadataReader.IsSupplementDsx(normalizedEntryPath))
            {
                await using var supplementStream = await entry.OpenEntryStreamAsync(cancellationToken);
                using var reader = new StreamReader(supplementStream);
                displayName = DazProductMetadataReader.TryReadProductName(await reader.ReadToEndAsync(cancellationToken));
                continue;
            }

            if (TryGetInstalledRelativePath(normalizedEntryPath, out var installedRelativePath, out var detectedRoot))
            {
                contentRoot ??= detectedRoot;
                files.Add(new DazArchiveScanEntry
                {
                    ArchiveRelativePath = normalizedEntryPath,
                    InstalledRelativePath = installedRelativePath,
                    FileSize = entry.Size < 0 ? 0UL : (ulong)entry.Size
                });

                AddClassification(installedRelativePath, categories, assetTypes);
                continue;
            }

            if (!IsNestedArchive(normalizedEntryPath))
                continue;

            var nestedArchivePath = Path.Combine(workingDirectory,
                $"{Guid.NewGuid():N}{Path.GetExtension(normalizedEntryPath)}");
            await using (var sourceStream = await entry.OpenEntryStreamAsync(cancellationToken))
            await using (var outputStream = File.Create(nestedArchivePath))
            {
                await sourceStream.CopyToAsync(outputStream, cancellationToken);
            }

            var nestedInfo = new FileInfo(nestedArchivePath);
            using var nestedArchive = await Task
                .Run(() => ArchiveFactory.OpenArchive(nestedArchivePath), cancellationToken)
                .ConfigureAwait(false);
            var nestedScan = await ScanOpenedArchiveAsync(nestedArchive, Path.GetFileName(normalizedEntryPath),
                normalizedEntryPath, (ulong)nestedInfo.Length, workingDirectory, cancellationToken)
                .ConfigureAwait(false);
            nestedArchives.Add(nestedScan);

            foreach (var category in nestedScan.Categories)
                categories.Add(category);
            foreach (var assetType in nestedScan.AssetTypes)
                assetTypes.Add(assetType);
        }

        return new DazArchiveScanResult
        {
            ArchiveName = archiveName,
            ArchivePath = archivePath,
            DisplayName = displayName,
            ArchiveSize = archiveSize,
            ContentRoot = contentRoot,
            AssetTypes = assetTypes.OrderBy(x => x).ToList(),
            Categories = categories.OrderBy(x => x).ToList(),
            InstallableFiles = files,
            NestedArchives = nestedArchives
        };
    }

    public static bool TryGetInstalledRelativePath(string archiveRelativePath, out string installedRelativePath,
        out string contentRoot)
    {
        var segments = archiveRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (!AssetRootDirectories.Contains(segments[index]))
                continue;

            installedRelativePath = CanonicalizeInstalledRelativePath(Path.Combine(segments[index..]));
            contentRoot = index == 0 ? string.Empty : string.Join('/', segments[..index]);
            return true;
        }

        installedRelativePath = string.Empty;
        contentRoot = string.Empty;
        return false;
    }

    public static string CanonicalizeInstalledRelativePath(string installedRelativePath, string? libraryRoot = null)
    {
        if (string.IsNullOrWhiteSpace(installedRelativePath))
            return installedRelativePath;

        var segments = installedRelativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/'],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return installedRelativePath;

        var resolvedSegments = new string[segments.Length];
        var currentPath = string.IsNullOrWhiteSpace(libraryRoot) ? null : Path.GetFullPath(libraryRoot);

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            resolvedSegments[index] = currentPath is null
                ? ResolveCanonicalSegmentName(segment)
                : ResolveSegmentAgainstLibrary(currentPath, segment);
            currentPath = currentPath is null ? null : Path.Combine(currentPath, resolvedSegments[index]);
        }

        return Path.Combine(resolvedSegments);
    }

    public static string ResolveCanonicalSegmentName(string segment)
    {
        return CanonicalSegmentNames.TryGetValue(segment, out var canonical) ? canonical : segment;
    }

    private static string ResolveSegmentAgainstLibrary(string parentPath, string segment)
    {
        if (Directory.Exists(parentPath))
        {
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(parentPath))
            {
                var entryName = Path.GetFileName(entryPath);
                if (string.Equals(entryName, segment, StringComparison.OrdinalIgnoreCase))
                    return entryName;
            }
        }

        return ResolveCanonicalSegmentName(segment);
    }

    public static bool IsNestedArchive(string archiveRelativePath)
    {
        var extension = Path.GetExtension(archiveRelativePath);
        return NestedArchiveExtensions.Contains(extension);
    }

    public static string NormalizeArchivePath(string key)
    {
        return key.Replace('\\', '/').TrimStart('/').Trim();
    }

    public static bool ShouldIgnoreArchiveEntry(string normalizedEntryPath)
    {
        return IgnoredFileNames.Contains(Path.GetFileName(normalizedEntryPath));
    }

    private static void AddClassification(string path, ISet<string> categories, ISet<AssetType> assetTypes)
    {
        foreach (var part in path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var (folder, assetType) in FolderToAssetType)
            {
                if (!string.Equals(part, folder, StringComparison.OrdinalIgnoreCase))
                    continue;

                categories.Add(NormalizeCategory(folder));
                assetTypes.Add(assetType);
                break;
            }
        }
    }
}