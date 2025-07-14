using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using SharpSevenZip;
using SharpSevenZip.Exceptions;

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

    private static readonly string[] StandardAssetsBasePaths =
        ["data", "documentation", "people", "props", "environments", "runtime", "scene", "scripts"];

    private readonly string _archivePath;
    private readonly DirectoryInfo _workingPath;
    private readonly LoadedArchive? _parentArchive;

    public DazArchiveLoader(string archivePath)
    {
        _archivePath = archivePath;
        _workingPath = Directory.CreateTempSubdirectory("DazLoader-" + Path.GetFileNameWithoutExtension(archivePath));
    }

    public DazArchiveLoader(string archivePath, string workingPathOverride, LoadedArchive parentArchive)
    {
        _archivePath = archivePath;
        _workingPath = new DirectoryInfo(workingPathOverride);
        _parentArchive = parentArchive;
    }

    public async Task<IEnumerable<LoadedArchive>> LoadArchiveAsync(IProgress<string>? progress = null)
    {
        var archives = new List<LoadedArchive>();

        var archiveName = Path.GetFileName(_archivePath);
        progress?.Report("Reading archive: " + archiveName);

        using var archive = new SharpSevenZipExtractor(_archivePath);
        if (!archive.Check())
            throw new ExtractionFailedException("Archie file could not be read or is corrupted.");

        await archive.ExtractArchiveAsync(_workingPath.FullName);

        progress?.Report($"Analyzing {archiveName} contents...");

        var filePath = _parentArchive is not null
            ? GetSubArchivePath(_archivePath, _parentArchive)
            : _archivePath;

        var loadedArchive = new LoadedArchive(Path.GetFileNameWithoutExtension(_archivePath), filePath,
            archive.UnpackedSize, _parentArchive, DigArchiveBaseDirectory(archive.ArchiveFileNames));

        if (!archive.ArchiveFileData.Where(d => !d.IsDirectory)
                .All(d => PackagedArchiveFileExtensions.Contains(Path.GetExtension(d.FileName))))
        {
            progress?.Report($"Reading {archiveName} contents...");

            if (IsTemplateArchive(archive))
                return archives;

            await HandleArchiveAsync(loadedArchive, archive, progress);
        }

        var subArchives = archive.ArchiveFileData
            .Where(d => ArchiveFileExtensions.Contains(Path.GetExtension(d.FileName)));

        foreach (var subArchive in subArchives)
        {
            var subArchiveFile = new FileInfo(Path.Combine(_workingPath.FullName, subArchive.FileName));

            var subArchiveWorkingDirectory = Path.Combine(subArchiveFile.DirectoryName!,
                Path.GetFileNameWithoutExtension(subArchiveFile.Name));
            Directory.CreateDirectory(subArchiveWorkingDirectory);

            using var subArchiveLoader =
                new DazArchiveLoader(subArchiveFile.FullName, subArchiveWorkingDirectory, loadedArchive);
            archives.AddRange(await subArchiveLoader.LoadArchiveAsync(progress));
        }

        return [..archives, loadedArchive];
    }

    public void Dispose()
    {
        _workingPath.Delete(true);
    }

    private static string GetSubArchivePath(string archivePath, LoadedArchive parentArchive)
    {
        var splitter = archivePath.Split(Path.DirectorySeparatorChar)
            .First(p => p.Contains(Path.GetFileNameWithoutExtension(parentArchive.FilePath)));

        var index = archivePath.IndexOf(splitter, StringComparison.Ordinal) + splitter.Length;
        return archivePath[index..].Trim(Path.DirectorySeparatorChar);
    }

    private static bool IsTemplateArchive(SharpSevenZipExtractor archive)
    {
        var fileNames = archive.ArchiveFileNames;
        return !StandardAssetsBasePaths.Any(b =>
            fileNames.Any(f => f.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                               f.Contains($"{Path.DirectorySeparatorChar}{b}{Path.DirectorySeparatorChar}",
                                   StringComparison.OrdinalIgnoreCase)));
    }
    
    private static string? DigArchiveBaseDirectory(IEnumerable<string> fileNames, string? starter = null)
    {
        var depth = 0;
        while (true)
        {
            if (depth > 10) return starter;

            var basePaths = fileNames.GroupBy(d => d.Split(Path.DirectorySeparatorChar).First()).ToList();
            if (StandardAssetsBasePaths.Any(b =>
                    basePaths.Any(p => p.Key.Equals(b, StringComparison.OrdinalIgnoreCase)))) return starter;

            var deeperLevel =
                basePaths.SelectMany(g => g.Select(p => p.Split(g.Key + Path.DirectorySeparatorChar).Last()));
            fileNames = deeperLevel;
            starter = basePaths.First().Key;
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

    private static async Task ExtractMetadataAsync(LoadedArchive loadedArchive, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        foreach (var metadataFile in MetadataFiles)
        {
            var archiveFileInfo = archiveFile.ArchiveFileData.FirstOrDefault(e =>
                Path.GetFileName(e.FileName).Equals(metadataFile, StringComparison.OrdinalIgnoreCase));

            if (archiveFileInfo == default || archiveFileInfo.Size >= 100000) continue; // Reasonable size limit

            progress?.Report($"Reading {metadataFile}...");
            var content = await ReadTextFromEntryAsync(archiveFile, archiveFileInfo);
            loadedArchive.Metadata[archiveFileInfo.FileName] = content;

            if (metadataFile.EndsWith("Supplement.dsx"))
                loadedArchive.Metadata["ProductName"] = GetProductName(loadedArchive, content);
        }

        // Analyze folder structure for better naming
        ImproveAssetNaming(loadedArchive);
    }

    private static async Task<string> ReadTextFromEntryAsync(SharpSevenZipExtractor archive,
        ArchiveFileInfo archiveFileInfo)
    {
        await using var memoryStream = new MemoryStream((int)archiveFileInfo.Size);
        await archive.ExtractFileAsync(archiveFileInfo.Index, memoryStream);

        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
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

    private async Task HandleArchiveAsync(LoadedArchive loadedArchive, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        AnalyzeZipContents(loadedArchive, archiveFile, progress);

        progress?.Report($"Extracting {loadedArchive.Name} metadata...");
        await ExtractMetadataAsync(loadedArchive, archiveFile, progress);

        loadedArchive.ArchiveStatus = ArchiveStatus.Ready;
    }

    private void AnalyzeZipContents(LoadedArchive loadedArchive, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        var categories = new HashSet<string>();
        var assetTypes = new HashSet<AssetType>();
        var fileCount = 0;

        foreach (var fileInfo in archiveFile.ArchiveFileData.Where(f => !f.IsDirectory))
        {
            fileCount++;
            loadedArchive.ContainedFiles.Add(new AssetFile
            {
                FileName = fileInfo.FileName,
                FileSize = fileInfo.Size
            });

            var pathParts = fileInfo.FileName.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in pathParts)
            {
                var lowerPart = part.ToLowerInvariant();

                foreach (var kvp in _folderToAssetType.Where(kvp => lowerPart.Contains(kvp.Key)))
                {
                    assetTypes.Add(kvp.Value);
                    categories.Add(kvp.Key);
                }
            }

            var extension = Path.GetExtension(fileInfo.FileName);
            if (_dazFileExtensions.Contains(extension))
                loadedArchive.Metadata[$"Has{extension.TrimStart('.').ToUpper()}Files"] = true;

            if (fileCount % 100 == 0)
                progress?.Report($"Analyzed {fileCount} files...");
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
}