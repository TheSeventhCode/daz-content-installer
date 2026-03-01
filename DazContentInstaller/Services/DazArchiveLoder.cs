using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace DazContentInstaller.Services;

public class DazArchiveLoader : IDisposable
{
    private readonly Dictionary<string, AssetType> _folderToAssetType = new()
    {
        { "characters", AssetType.Character },
        { "anatomy", AssetType.Anatomy },
        { "clothing", AssetType.Clothing },
        { "wardrobe", AssetType.Clothing },
        { "hair", AssetType.Hair },
        { "props", AssetType.Props },
        { "vehicles", AssetType.Props },
        { "environments", AssetType.Environment },
        { "scenes", AssetType.Environment },
        { "poses", AssetType.Poses },
        { "expressions", AssetType.Poses },
        { "animations", AssetType.Poses },
        { "materials", AssetType.Materials },
        { "shaders", AssetType.Materials },
        { "morphs", AssetType.Morphs },
        { "lights", AssetType.Lights },
        { "cameras", AssetType.Cameras },
        { "scripts", AssetType.Scripts },
        { "textures", AssetType.Textures }
    };

    private readonly HashSet<string> _dazFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".duf", ".dsf", ".dse", ".dsa", ".daz", ".pz2", ".cr2", ".pp2", ".hr2",
        ".fc2", ".hd2", ".lt2", ".cm2", ".mc6", ".mt5", ".mat", ".obj", ".fbx"
    };

    private static readonly string[] MetadataFiles = ["Supplement.dsx", "manifest.json", "ProductInformation.json"];
    private static readonly string[] ArchiveFileExtensions = [".zip", ".rar", ".7z"];

    private static readonly string[] PackagedArchiveFileExtensions =
        [".jpg", ".png", ".zip", ".rar", ".txt"];

    // "Documentation" is not part of this on purpose, as some people have a separate documentation directory in their template archive.
    private static readonly string[] StandardAssetsBasePaths =
        ["data", "people", "props", "environments", "runtime", "scene", "scripts"];

    private readonly string _archivePath;
    private readonly DirectoryInfo _workingPath;
    private readonly string _baseTemporaryWorkingDirectory;
    private readonly LoadedArchive? _parentArchive;

    public DazArchiveLoader(string archivePath)
    {
        _archivePath = archivePath;
        _workingPath = Directory.CreateTempSubdirectory("DazLoader-" + Path.GetFileNameWithoutExtension(archivePath));
        _baseTemporaryWorkingDirectory = _workingPath.FullName;
    }

    private DazArchiveLoader(string archivePath, string workingPathOverride, LoadedArchive parentArchive, string baseTemporaryWorkingDirectory)
    {
        _archivePath = archivePath;
        _workingPath = new DirectoryInfo(workingPathOverride);
        _parentArchive = parentArchive;
        _baseTemporaryWorkingDirectory = baseTemporaryWorkingDirectory;
    }

    public async Task<IEnumerable<LoadedArchive>> LoadArchiveAsync(IProgress<string>? messageProgress = null,
        IProgress<int>? percentProgress = null)
    {
        var archives = new List<LoadedArchive>();

        var archiveName = Path.GetFileName(_archivePath);

        await ExtractArchiveAsync(_archivePath, _workingPath.FullName);
        messageProgress?.Report("Extracted archive: " + archiveName);

        var filePath = _parentArchive is not null
            ? GetSubArchivePath(_archivePath, _parentArchive)
            : _archivePath;

        var fileName = _parentArchive is not null
            ? Path.Combine(
                _parentArchive.ParentArchive is null
                    ? Path.GetFileName(_parentArchive.FilePath)
                    : _parentArchive.Name, Path.GetFileName(_archivePath))
            : Path.GetFileNameWithoutExtension(_archivePath);
        var extractedFiles = EnumerateExtractedFiles(_workingPath.FullName).ToList();
        ulong unpackedSize = 0;
        foreach (var extractedFile in extractedFiles)
            unpackedSize += extractedFile.FileSize;
        var loadedArchive = new LoadedArchive(fileName, filePath,
            (long)unpackedSize, _parentArchive,
            DigArchiveBaseDirectory(extractedFiles.Select(e => e.RelativePath)));

        if (!extractedFiles
                .All(d => PackagedArchiveFileExtensions.Contains(Path.GetExtension(d.RelativePath).ToLowerInvariant())) &&
            !IsTemplateArchive(extractedFiles.Select(f => f.RelativePath)))
        {
            await HandleArchiveAsync(loadedArchive, extractedFiles, messageProgress, percentProgress);
            messageProgress?.Report($"Analyzed {archiveName}");
        }

        var subArchives = extractedFiles
            .Where(d => ArchiveFileExtensions.Contains(Path.GetExtension(d.RelativePath).ToLowerInvariant()))
            .ToList();

        var increment = subArchives.Count == 0 ? 100D : Math.Ceiling(100D / subArchives.Count);
        var progress = 0;
        percentProgress?.Report(0);
        foreach (var subArchive in subArchives)
        {
            var subArchiveFile = new FileInfo(subArchive.FullPath);

            var subArchiveWorkingDirectory = Path.Combine(subArchiveFile.DirectoryName!,
                Path.GetFileNameWithoutExtension(subArchiveFile.Name));
            Directory.CreateDirectory(subArchiveWorkingDirectory);

            using var subArchiveLoader =
                new DazArchiveLoader(subArchiveFile.FullName, subArchiveWorkingDirectory, loadedArchive, _baseTemporaryWorkingDirectory);
            archives.AddRange(await subArchiveLoader.LoadArchiveAsync(messageProgress));
            progress += (int)increment;
            percentProgress?.Report(progress);
        }

        percentProgress?.Report(100);

        return [..archives, loadedArchive];
    }

    public void Dispose()
    {
        _workingPath.Delete(true);
    }

    private string GetSubArchivePath(string archivePath, LoadedArchive parentArchive)
    {
        var cut = archivePath[_baseTemporaryWorkingDirectory.Length..].TrimStart(Path.DirectorySeparatorChar);
        if (parentArchive.ParentArchive is null)
            return cut;

        var parentName = Path.GetFileNameWithoutExtension(parentArchive.FilePath);
        var parentIndex = cut.IndexOf(parentName, StringComparison.OrdinalIgnoreCase);
        if(parentIndex > -1)
            cut = cut.Substring(parentIndex + parentName.Length).TrimStart(Path.DirectorySeparatorChar);

        return cut;
    }

    private static string GetFullParentArchivePath(LoadedArchive parentArchive)
    {
        return parentArchive.ParentArchive is null
            ? Path.GetFileNameWithoutExtension(parentArchive.FilePath)
            : Path.Combine(GetFullParentArchivePath(parentArchive.ParentArchive), parentArchive.FilePath);
    }

    private static bool IsTemplateArchive(IEnumerable<string> fileNames)
    {
        var normalizedFileNames = fileNames.Select(NormalizeArchivePath).ToList();
        return !StandardAssetsBasePaths.Any(b =>
            normalizedFileNames.Any(f => f.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                               f.Contains($"/{b}/",
                                   StringComparison.OrdinalIgnoreCase)));
    }

    private static string? DigArchiveBaseDirectory(IEnumerable<string> fileNames, string? starter = null)
    {
        var depth = 0;
        while (true)
        {
            if (depth > 10) return starter?.Trim(Path.DirectorySeparatorChar);

            var basePaths = fileNames.GroupBy(d => SplitArchivePath(d).FirstOrDefault() ?? string.Empty).ToList();
            if (StandardAssetsBasePaths.Any(b =>
                    basePaths.Any(p => p.Key.Equals(b, StringComparison.OrdinalIgnoreCase))))
                return starter?.Trim(Path.DirectorySeparatorChar);

            var deeperLevel =
                basePaths.SelectMany(g => g.Select(p =>
                {
                    var normalized = NormalizeArchivePath(p);
                    return normalized.StartsWith(g.Key + "/", StringComparison.OrdinalIgnoreCase)
                        ? normalized[(g.Key.Length + 1)..]
                        : normalized;
                }));
            fileNames = deeperLevel;
            starter += Path.DirectorySeparatorChar + basePaths.FirstOrDefault(d =>
                d.Any(b => SplitArchivePath(b).Any(s =>
                    StandardAssetsBasePaths.Any(p => p.Equals(s, StringComparison.OrdinalIgnoreCase)))))?.Key;

            starter ??= basePaths.First().Key;
            depth++;
        }
    }

    private static AssetType DetermineAssetType(HashSet<AssetType> detectedTypes)
    {
        return detectedTypes.Count switch
        {
            0 => AssetType.Unknown,
            1 => detectedTypes.First(),
            _ => AssetType.Mixed
        };
    }

    private static async Task ExtractMetadataAsync(LoadedArchive loadedArchive, IReadOnlyCollection<ExtractedFile> files,
        IProgress<string>? progress)
    {
        foreach (var metadataFile in MetadataFiles)
        {
            var extractedMetadataFile = files
                .FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals(metadataFile, StringComparison.OrdinalIgnoreCase));

            if (extractedMetadataFile is null || extractedMetadataFile.FileSize >= 100000) continue; // Reasonable size limit

            progress?.Report($"Reading {metadataFile}...");
            var content = await File.ReadAllTextAsync(extractedMetadataFile.FullPath, Encoding.UTF8);
            loadedArchive.Metadata[extractedMetadataFile.RelativePath] = content;

            if (metadataFile.EndsWith("Supplement.dsx"))
            {
                var name = GetProductName(loadedArchive, content);
                loadedArchive.Metadata["ProductName"] = loadedArchive.Name = name;
            }
        }

        // Analyze folder structure for better naming
        ImproveAssetNaming(loadedArchive);
    }

    private static string GetProductName(LoadedArchive loadedArchive, string xmlContent)
    {
        try
        {
            var contentRoot = XDocument.Parse(xmlContent).Root!;
            return contentRoot.Element("ProductName")!.Attribute("VALUE")!.Value;
        }
        catch (Exception ex)
        {
            loadedArchive.ArchiveStatus = ArchiveStatus.Error;
            loadedArchive.Metadata["Error"] = ex.Message;
            return string.Empty;
        }
    }

    private static void ImproveAssetNaming(LoadedArchive loadedArchive)
    {
        // Try to improve asset naming based on folder structure
        var topLevelFolders = loadedArchive.ContainedFiles
            .Where(f => !f.FileName.StartsWith("__MACOSX")) // Ignore Mac metadata
            .Select(f => f.FileName.Split('/')[0])
            .Where(f => !string.IsNullOrEmpty(f))
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (topLevelFolders != null && topLevelFolders.Key != "Content" && topLevelFolders.Key != "content")
        {
            // If there's a consistent top-level folder that's not "Content", use it
            var folderName = topLevelFolders.Key;
            if (folderName.Length > 3 && !folderName.Equals(loadedArchive.Name, StringComparison.OrdinalIgnoreCase))
            {
                loadedArchive.Metadata["SuggestedName"] = folderName;
            }
        }

        // Look for product name in folder structure
        var productFolders = loadedArchive.ContainedFiles
            .Where(f => f.FileName.Contains("Product", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetDirectoryName(f.FileName))
            .FirstOrDefault(d => !string.IsNullOrEmpty(d));

        if (!string.IsNullOrEmpty(productFolders))
        {
            loadedArchive.Metadata["ProductFolder"] = productFolders;
        }
    }

    private async Task HandleArchiveAsync(LoadedArchive loadedArchive, IReadOnlyCollection<ExtractedFile> files,
        IProgress<string>? messageProgress, IProgress<int>? percentProgress)
    {
        AnalyzeZipContents(loadedArchive, files, messageProgress, percentProgress);

        await ExtractMetadataAsync(loadedArchive, files, messageProgress);
        messageProgress?.Report($"Extracted {loadedArchive.Name} metadata...");

        loadedArchive.ArchiveStatus = ArchiveStatus.Ready;
    }

    private void AnalyzeZipContents(LoadedArchive loadedArchive, IReadOnlyCollection<ExtractedFile> files,
        IProgress<string>? messageProgress, IProgress<int>? percentProgress)
    {
        var categories = new HashSet<string>();
        var assetTypes = new HashSet<AssetType>();
        var fileCount = 0;

        foreach (var fileInfo in files)
        {
            fileCount++;
            var normalizedPath = fileInfo.RelativePath;
            loadedArchive.ContainedFiles.Add(new AssetFile
            {
                FileName = normalizedPath,
                FileSize = fileInfo.FileSize
            });

            var pathParts = SplitArchivePath(normalizedPath);

            foreach (var part in pathParts)
            {
                var lowerPart = part.ToLowerInvariant();

                foreach (var kvp in _folderToAssetType.Where(kvp => lowerPart.Contains(kvp.Key)))
                {
                    assetTypes.Add(kvp.Value);
                    categories.Add(kvp.Key);
                }
            }

            var extension = Path.GetExtension(normalizedPath);
            if (_dazFileExtensions.Contains(extension.ToLower()))
                loadedArchive.Metadata[$"Has{extension.TrimStart('.').ToUpper()}Files"] = true;

            if (fileCount % 100 == 0)
                messageProgress?.Report($"Analyzed {fileCount} files...");
        }

        foreach (var category in categories)
            loadedArchive.Categories.Add(category);
        loadedArchive.AssetType = DetermineAssetType(loadedArchive.AssetType is AssetType.Unknown
            ? assetTypes
            : ( [..assetTypes, loadedArchive.AssetType]));

        if (loadedArchive.Metadata.TryGetValue("FileCount", out var existingCount))
            loadedArchive.Metadata["FileCount"] = (int)existingCount + fileCount;
        else
            loadedArchive.Metadata["FileCount"] = fileCount;
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destination)
    {
        await using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            await Task.Run(() => reader.WriteEntryToDirectory(destination));
        }
    }

    private static string NormalizeArchivePath(string path) =>
        path.Replace('\\', '/');

    private static string[] SplitArchivePath(string path) =>
        NormalizeArchivePath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<ExtractedFile> EnumerateExtractedFiles(string rootPath)
    {
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            yield return new ExtractedFile(
                FullPath: file,
                RelativePath: NormalizeArchivePath(Path.GetRelativePath(rootPath, file)),
                FileSize: (ulong)new FileInfo(file).Length);
        }
    }

    private sealed record ExtractedFile(string FullPath, string RelativePath, ulong FileSize);
}