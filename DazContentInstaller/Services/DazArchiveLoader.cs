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

namespace DazContentInstaller.Services;

public class DazArchiveLoader : IDisposable
{
    private readonly DirectoryInfo _tempDirectory;
    private readonly string _baseArchivePath;

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
        { "lights", AssetType.Lights },
        { "cameras", AssetType.Cameras },
        { "scripts", AssetType.Scripts }
    };

    private readonly HashSet<string> _dazFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".duf", ".dsf", ".dse", ".dsa", ".daz", ".pz2", ".cr2", ".pp2", ".hr2",
        ".fc2", ".hd2", ".lt2", ".cm2", ".mc6", ".mt5", ".mat", ".obj", ".fbx"
    };

    private readonly HashSet<string> _packagedArchiveFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".png", ".zip", ".rar"
    };

    private static readonly string[] MetadataFiles = ["Supplement.dsx", "manifest.json", "ProductInformation.json"];

    public DazArchiveLoader(string baseArchivePath)
    {
        _tempDirectory = Directory.CreateTempSubdirectory("DazContentLoader");
        _baseArchivePath = baseArchivePath;
    }

    public async Task<List<LoadedArchive>> LoadArchiveAsync(IProgress<string>? progress = null)
    {
        var archives = new List<LoadedArchive>();

        progress?.Report("Reading Archive...");
        using var archiveFile = new SharpSevenZipExtractor(_baseArchivePath);

        await archiveFile.ExtractArchiveAsync(_tempDirectory.FullName);

        progress?.Report("Analyzing contents...");

        if (archiveFile.ArchiveFileNames.All(d => _packagedArchiveFileExtensions.Contains(Path.GetExtension(d))))
            archives.AddRange(await DigSubArchivesAsync(progress));
        else
        {
            var archive = new LoadedArchive
            {
                FilePath = _baseArchivePath,
                Name = Path.GetFileNameWithoutExtension(_baseArchivePath),
                Status = ArchiveStatus.Loading
            };

            progress?.Report("Reading contents...");
            await HandleArchiveAsync(archive, archiveFile, progress);
            archives.Add(archive);
        }

        return archives;
    }

    public void Dispose()
    {
        _tempDirectory.Delete(true);
    }

    private async Task<List<LoadedArchive>> DigSubArchivesAsync(IProgress<string>? progress)
    {
        var fileIndex = 0;
        var archives = new List<LoadedArchive>();

        foreach (var subarchive in _tempDirectory.GetFiles().Where(fi => Path.GetExtension(fi.Name).EndsWith("zip")))
        {
            fileIndex++;
            progress?.Report($"Reading sub-archive {fileIndex}...");

            var archive = new LoadedArchive
            {
                FilePath = Path.Combine(_baseArchivePath, subarchive.Name),
                Name = Path.Combine(Path.GetFileNameWithoutExtension(_baseArchivePath), subarchive.Name),
                Status = ArchiveStatus.Loading
            };

            var subArchive = new SharpSevenZipExtractor(subarchive.FullName);
            if (subArchive.ArchiveFileData.First().FileName.Equals("Templates", StringComparison.OrdinalIgnoreCase))
                continue;

            await HandleArchiveAsync(archive, subArchive, progress);
            archives.Add(archive);
        }

        return archives;
    }

    private async Task HandleArchiveAsync(LoadedArchive archive, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        var fileInfo = new FileInfo(archiveFile.FileName!);
        archive.FileSizeBytes = fileInfo.Length;

        AnalyzeZipContents(archive, archiveFile, progress);

        progress?.Report("Extracting metadata...");
        await ExtractMetadataAsync(archive, archiveFile, progress);

        archive.Status = ArchiveStatus.Ready;
    }

    private void AnalyzeZipContents(LoadedArchive archive, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        var categories = new HashSet<string>();
        var assetTypes = new HashSet<AssetType>();
        var fileCount = 0;

        foreach (var fileInfo in archiveFile.ArchiveFileData)
        {
            fileCount++;
            archive.ContainedFiles.Add(fileInfo.FileName);

            var pathParts = fileInfo.FileName.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in pathParts)
            {
                var lowerPart = part.ToLowerInvariant();

                foreach (var kvp in _folderToAssetType.Where(kvp => lowerPart.Contains(kvp.Key)))
                {
                    assetTypes.Add(kvp.Value);
                    categories.Add(kvp.Key);
                }

                // if (!GenesisRegex().IsMatch(lowerPart)) continue;
                //
                // assetTypes.Add(AssetType.Character);
                // categories.Add("genesis");
            }

            var extension = Path.GetExtension(fileInfo.FileName);
            if (_dazFileExtensions.Contains(extension))
                archive.Metadata[$"Has{extension.TrimStart('.').ToUpper()}Files"] = true;

            if (fileCount % 100 == 0)
                progress?.Report($"Analyzed {fileCount} files...");
        }

        foreach (var category in categories)
            archive.Categories.Add(category);
        archive.AssetType = DetermineAssetType(archive.AssetType is AssetType.Unknown
            ? assetTypes
            : ( [..assetTypes, archive.AssetType]));

        if (archive.Metadata.TryGetValue("FileCount", out var existingCount))
            archive.Metadata["FileCount"] = (int)existingCount + fileCount;
        else
            archive.Metadata["FileCount"] = fileCount;
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

    private static async Task ExtractMetadataAsync(LoadedArchive archive, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        foreach (var metadataFile in MetadataFiles)
        {
            var archiveFileInfo = archiveFile.ArchiveFileData.FirstOrDefault(e =>
                Path.GetFileName(e.FileName).Equals(metadataFile, StringComparison.OrdinalIgnoreCase));

            if (archiveFileInfo == default || archiveFileInfo.Size >= 100000) continue; // Reasonable size limit

            progress?.Report($"Reading {metadataFile}...");
            var content = await ReadTextFromEntryAsync(archiveFile, archiveFileInfo);
            archive.Metadata[$"{archiveFileInfo.FileName}/{metadataFile}"] = content;

            if (metadataFile.EndsWith("Supplement.dsx"))
                archive.Metadata[$"{archiveFileInfo.FileName}_ProductName"] = GetProductName(archive, content);
        }

        // Analyze folder structure for better naming
        ImproveAssetNaming(archive);
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

    private static string GetProductName(LoadedArchive archive, string xmlContent)
    {
        try
        {
            var contentRoot = XDocument.Parse(xmlContent).Root!;
            return contentRoot.Element("ProductName")!.Attribute("VALUE")!.Value;
        }
        catch (Exception ex)
        {
            archive.Status = ArchiveStatus.Error;
            archive.Metadata["Error"] = ex.Message;
            return string.Empty;
        }
    }

    private static void ImproveAssetNaming(LoadedArchive archive)
    {
        // Try to improve asset naming based on folder structure
        var topLevelFolders = archive.ContainedFiles
            .Where(f => !f.StartsWith("__MACOSX")) // Ignore Mac metadata
            .Select(f => f.Split('/')[0])
            .Where(f => !string.IsNullOrEmpty(f))
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (topLevelFolders != null && topLevelFolders.Key != "Content" && topLevelFolders.Key != "content")
        {
            // If there's a consistent top-level folder that's not "Content", use it
            var folderName = topLevelFolders.Key;
            if (folderName.Length > 3 && !folderName.Equals(archive.Name, StringComparison.OrdinalIgnoreCase))
            {
                archive.Metadata["SuggestedName"] = folderName;
            }
        }

        // Look for product name in folder structure
        var productFolders = archive.ContainedFiles
            .Where(f => f.Contains("Product", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(d => !string.IsNullOrEmpty(d));

        if (!string.IsNullOrEmpty(productFolders))
        {
            archive.Metadata["ProductFolder"] = productFolders;
        }
    }
}