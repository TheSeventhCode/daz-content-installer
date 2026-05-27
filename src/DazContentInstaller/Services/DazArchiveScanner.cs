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
    private static readonly HashSet<string> AssetRootDirectories = new(StringComparer.OrdinalIgnoreCase)
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
        "aniBlocks",
        "vehicles"
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

    private static readonly HashSet<string> ThumbnailImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp",
        ".gif"
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
        ["shader presets"] = AssetType.Shaders,
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
        return await ScanArchiveFileAsync(archivePath, workingDirectory.DirectoryInfo.FullName, cancellationToken);
    }

    private static async Task<DazArchiveScanResult> ScanArchiveFileAsync(string archivePath, string workingDirectory,
        CancellationToken cancellationToken)
    {
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists)
            throw new FileNotFoundException("Archive file not found.", archivePath);

        using var sourceArchive = await Task
            .Run(() => ArchiveFactory.OpenArchive(archiveInfo.FullName), cancellationToken);
        return await ScanOpenedArchiveAsync(sourceArchive, archiveInfo.Name, archiveInfo.FullName,
            (ulong)archiveInfo.Length,
            workingDirectory, captureTopLevelThumbnail: true, cancellationToken);
    }

    private static async Task<DazArchiveScanResult> ScanOpenedArchiveAsync(IArchive sourceArchive, string archiveName,
        string archivePath, ulong archiveSize, string workingDirectory, bool captureTopLevelThumbnail,
        CancellationToken cancellationToken)
    {
        var scanState = new ArchiveScanState();

        foreach (var entry in sourceArchive.Entries.Where(x => !x.IsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedEntryPath = NormalizeArchivePath(entry.Key ?? string.Empty);
            if (ShouldSkipEntry(normalizedEntryPath))
                continue;

            await ScanArchiveEntryAsync(entry, normalizedEntryPath, scanState, workingDirectory,
                    captureTopLevelThumbnail, cancellationToken);
        }

        return scanState.ToScanResult(archiveName, archivePath, archiveSize);
    }

    private static bool ShouldSkipEntry(string normalizedEntryPath)
    {
        return string.IsNullOrWhiteSpace(normalizedEntryPath)
               || ShouldIgnoreArchiveEntry(normalizedEntryPath);
    }

    private static async Task ScanArchiveEntryAsync(IArchiveEntry entry, string normalizedEntryPath,
        ArchiveScanState scanState, string workingDirectory, bool captureTopLevelThumbnail,
        CancellationToken cancellationToken)
    {
        if (await TryReadDisplayNameAsync(entry, normalizedEntryPath, scanState, cancellationToken))
            return;

        TryCaptureThumbnail(normalizedEntryPath, scanState, captureTopLevelThumbnail);

        if (TryAddInstallableFile(entry, normalizedEntryPath, scanState))
            return;

        if (IsNestedArchive(normalizedEntryPath))
            await ScanNestedArchiveAsync(entry, normalizedEntryPath, scanState, workingDirectory, cancellationToken);
    }

    private static async Task<bool> TryReadDisplayNameAsync(IArchiveEntry entry, string normalizedEntryPath,
        ArchiveScanState scanState, CancellationToken cancellationToken)
    {
        if (scanState.DisplayName is not null || !DazProductMetadataReader.IsSupplementDsx(normalizedEntryPath))
            return false;

        await using var supplementStream = await entry.OpenEntryStreamAsync(cancellationToken);
        using var reader = new StreamReader(supplementStream);
        scanState.DisplayName =
            DazProductMetadataReader.TryReadProductName(await reader.ReadToEndAsync(cancellationToken));
        return true;
    }

    private static void TryCaptureThumbnail(string normalizedEntryPath, ArchiveScanState scanState,
        bool captureTopLevelThumbnail)
    {
        if (scanState.ThumbnailArchiveRelativePath is null
            && captureTopLevelThumbnail
            && IsTopLevelThumbnailCandidate(normalizedEntryPath))
            scanState.ThumbnailArchiveRelativePath = normalizedEntryPath;
    }

    private static bool TryAddInstallableFile(IArchiveEntry entry, string normalizedEntryPath,
        ArchiveScanState scanState)
    {
        if (!TryGetInstalledRelativePath(normalizedEntryPath, out var installedRelativePath, out var detectedRoot))
            return false;

        scanState.ContentRoot ??= detectedRoot;
        scanState.Files.Add(new DazArchiveScanEntry
        {
            ArchiveRelativePath = normalizedEntryPath,
            InstalledRelativePath = installedRelativePath,
            FileSize = entry.Size < 0 ? 0UL : (ulong)entry.Size
        });

        AddClassification(installedRelativePath, scanState.Categories, scanState.AssetTypes);
        return true;
    }

    private static async Task ScanNestedArchiveAsync(IArchiveEntry entry, string normalizedEntryPath,
        ArchiveScanState scanState, string workingDirectory, CancellationToken cancellationToken)
    {
        var nestedArchivePath = await ExtractNestedArchiveAsync(entry, normalizedEntryPath, workingDirectory,
                cancellationToken);
        var nestedInfo = new FileInfo(nestedArchivePath);

        using var nestedArchive = await Task
            .Run(() => ArchiveFactory.OpenArchive(nestedArchivePath), cancellationToken);
        var nestedScan = await ScanOpenedArchiveAsync(nestedArchive, Path.GetFileName(normalizedEntryPath),
                normalizedEntryPath, (ulong)nestedInfo.Length, workingDirectory, captureTopLevelThumbnail: false,
                cancellationToken);

        scanState.AddNestedArchive(nestedScan);
    }

    private static async Task<string> ExtractNestedArchiveAsync(IArchiveEntry entry, string normalizedEntryPath,
        string workingDirectory, CancellationToken cancellationToken)
    {
        var nestedArchivePath = Path.Combine(workingDirectory,
            $"{Guid.NewGuid():N}{Path.GetExtension(normalizedEntryPath)}");
        await using (var sourceStream = await entry.OpenEntryStreamAsync(cancellationToken))
        await using (var outputStream = File.Create(nestedArchivePath))
        {
            await sourceStream.CopyToAsync(outputStream, cancellationToken);
        }

        return nestedArchivePath;
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

    private static string ResolveCanonicalSegmentName(string segment)
    {
        return CanonicalSegmentNames.TryGetValue(segment, out var canonical) ? canonical : segment;
    }

    private static string ResolveSegmentAgainstLibrary(string parentPath, string segment)
    {
        if (!Directory.Exists(parentPath))
            return ResolveCanonicalSegmentName(segment);

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(parentPath))
        {
            var entryName = Path.GetFileName(entryPath);
            if (string.Equals(entryName, segment, StringComparison.OrdinalIgnoreCase))
                return entryName;
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

    private static bool IsTopLevelThumbnailCandidate(string normalizedEntryPath)
    {
        return !string.IsNullOrWhiteSpace(normalizedEntryPath)
               && !normalizedEntryPath.Contains('/')
               && ThumbnailImageExtensions.Contains(Path.GetExtension(normalizedEntryPath));
    }

    private static void AddClassification(string path, HashSet<string> categories, HashSet<AssetType> assetTypes)
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

    private sealed class ArchiveScanState
    {
        public List<DazArchiveScanEntry> Files { get; } = [];
        public List<DazArchiveScanResult> NestedArchives { get; } = [];
        public HashSet<string> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<AssetType> AssetTypes { get; } = [];
        public string? ContentRoot { get; set; }
        public string? DisplayName { get; set; }
        public string? ThumbnailArchiveRelativePath { get; set; }

        public void AddNestedArchive(DazArchiveScanResult nestedScan)
        {
            NestedArchives.Add(nestedScan);

            foreach (var category in nestedScan.Categories)
                Categories.Add(NormalizeCategory(category));
            foreach (var assetType in nestedScan.AssetTypes)
                AssetTypes.Add(assetType);
        }

        public DazArchiveScanResult ToScanResult(string archiveName, string archivePath, ulong archiveSize)
        {
            return new DazArchiveScanResult
            {
                ArchiveName = archiveName,
                ArchivePath = archivePath,
                DisplayName = DisplayName,
                ThumbnailArchiveRelativePath = ThumbnailArchiveRelativePath,
                ArchiveSize = archiveSize,
                ContentRoot = ContentRoot,
                AssetTypes = AssetTypes.OrderBy(x => x).ToList(),
                Categories = Categories.OrderBy(x => x).ToList(),
                InstallableFiles = Files,
                NestedArchives = NestedArchives
            };
        }
    }
}