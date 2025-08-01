﻿using System;
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

public class DazArchiveLoaderOld : IAsyncDisposable
{
    public DirectoryInfo TempDirectory { get; }
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

    private readonly HashSet<string> _packagedArchiveFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".png", ".zip", ".rar", ".txt"
    };

    private static readonly string[] StandardAssetsBasePaths =
        ["data", "documentation", "people", "props", "environments", "runtime", "scene", "scripts"];

    private static readonly string[] MetadataFiles = ["Supplement.dsx", "manifest.json", "ProductInformation.json"];
    private static readonly string[] ArchiveExtensions = ["zip", "rar", "7z"];

    public DazArchiveLoaderOld(string baseArchivePath)
    {
        TempDirectory = Directory.CreateTempSubdirectory("DazContentLoader");
        _baseArchivePath = baseArchivePath;
    }

    public async Task<List<LoadedArchiveOld>> LoadArchiveAsync(IProgress<string>? progress = null)
    {
        var archives = new List<LoadedArchiveOld>();

        var archiveName = Path.GetFileName(_baseArchivePath);
        progress?.Report($"Reading {archiveName}...");

        try
        {
            using var archiveFile = new SharpSevenZipExtractor(_baseArchivePath);
            if (!archiveFile.Check())
                throw new ExtractionFailedException("Archive file could not be read or is corrupted.");

            await archiveFile.ExtractArchiveAsync(TempDirectory.FullName);

            progress?.Report($"Analyzing {archiveName} contents...");

            if (archiveFile.ArchiveFileData
                .Where(d => !d.IsDirectory)
                .All(d => _packagedArchiveFileExtensions.Contains(
                    Path.GetExtension(d.FileName))))
                archives.AddRange(await DigSubArchivesAsync(progress));
            else
            {
                var archive = new LoadedArchiveOld
                {
                    FilePath = _baseArchivePath,
                    Name = Path.GetFileNameWithoutExtension(_baseArchivePath),
                    Status = ArchiveStatus.Loading,
                    IsPartOfParentArchive = false
                };

                progress?.Report($"Reading {archiveName} contents...");
                await HandleArchiveAsync(archive, archiveFile, progress);
                archives.Add(archive);
            }
        }
        catch (SharpSevenZipException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            throw;
        }


        return archives;
    }

    public async ValueTask DisposeAsync()
    {
        var retries = 0;
        while (true)
        {
            try
            {
                TempDirectory.Delete(true);
                break;
            }
            catch (IOException)
            {
                if (retries > 10)
                {
                    throw;
                }

                await Task.Delay(250);
                retries++;
            }
        }
    }

    public void Cleanup()
    {
        if (TempDirectory.Exists)
            TempDirectory.Delete(true);
    }

    private async Task<List<LoadedArchiveOld>> DigSubArchivesAsync(IProgress<string>? progress)
    {
        var archives = new List<LoadedArchiveOld>();

        foreach (var subarchive in TempDirectory.EnumerateFiles("*", SearchOption.AllDirectories)
                     .Where(fi => ArchiveExtensions.Any(e => Path.GetExtension(fi.Name).EndsWith(e))))
        {
            progress?.Report($"Reading sub-archive {subarchive.Name}...");

            var archive = new LoadedArchiveOld
            {
                FilePath = Path.Combine(_baseArchivePath, subarchive.Name),
                Name = Path.Combine(Path.GetFileNameWithoutExtension(_baseArchivePath), subarchive.Name),
                Status = ArchiveStatus.Loading,
                IsPartOfParentArchive = true
            };

            if (subarchive.DirectoryName != TempDirectory.FullName)
                archive.CustomSubArchiveDirectory = subarchive.Directory!.Name;

            using var subArchiveFile = new SharpSevenZipExtractor(subarchive.FullName);
            if (IsTemplateArchive(subArchiveFile))
                continue;

            await HandleArchiveAsync(archive, subArchiveFile, progress);
            archives.Add(archive);
        }

        return archives;
    }

    private static bool IsTemplateArchive(SharpSevenZipExtractor archive)
    {
        var fileNames = archive.ArchiveFileNames;
        return !StandardAssetsBasePaths.Any(b =>
            fileNames.Any(f => f.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                               f.Contains($"{Path.DirectorySeparatorChar}{b}{Path.DirectorySeparatorChar}",
                                   StringComparison.OrdinalIgnoreCase)));
    }

    private async Task HandleArchiveAsync(LoadedArchiveOld archiveOld, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        var fileInfo = new FileInfo(archiveFile.FileName!);
        archiveOld.FileSizeBytes = fileInfo.Length;

        archiveOld.CustomAssetBaseDirectory = DigArchiveBaseDirectory(archiveFile.ArchiveFileNames);

        AnalyzeZipContents(archiveOld, archiveFile, progress);

        progress?.Report($"Extracting {archiveOld.Name} metadata...");
        await ExtractMetadataAsync(archiveOld, archiveFile, progress);

        archiveOld.Status = ArchiveStatus.Ready;
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

    private void AnalyzeZipContents(LoadedArchiveOld archiveOld, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        var categories = new HashSet<string>();
        var assetTypes = new HashSet<AssetType>();
        var fileCount = 0;

        foreach (var fileInfo in archiveFile.ArchiveFileData.Where(i => !i.IsDirectory))
        {
            fileCount++;
            archiveOld.ContainedFiles.Add(new AssetFile
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
                archiveOld.Metadata[$"Has{extension.TrimStart('.').ToUpper()}Files"] = true;

            if (fileCount % 100 == 0)
                progress?.Report($"Analyzed {fileCount} files...");
        }

        foreach (var category in categories)
            archiveOld.Categories.Add(category);
        archiveOld.AssetType = DetermineAssetType(archiveOld.AssetType is AssetType.Unknown
            ? assetTypes
            : ( [..assetTypes, archiveOld.AssetType]));

        if (archiveOld.Metadata.TryGetValue("FileCount", out var existingCount))
            archiveOld.Metadata["FileCount"] = (int)existingCount + fileCount;
        else
            archiveOld.Metadata["FileCount"] = fileCount;
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

    private static async Task ExtractMetadataAsync(LoadedArchiveOld archiveOld, SharpSevenZipExtractor archiveFile,
        IProgress<string>? progress)
    {
        foreach (var metadataFile in MetadataFiles)
        {
            var archiveFileInfo = archiveFile.ArchiveFileData.FirstOrDefault(e =>
                Path.GetFileName(e.FileName).Equals(metadataFile, StringComparison.OrdinalIgnoreCase));

            if (archiveFileInfo == default || archiveFileInfo.Size >= 100000) continue; // Reasonable size limit

            progress?.Report($"Reading {metadataFile}...");
            var content = await ReadTextFromEntryAsync(archiveFile, archiveFileInfo);
            archiveOld.Metadata[archiveFileInfo.FileName] = content;

            if (metadataFile.EndsWith("Supplement.dsx"))
                archiveOld.Metadata["ProductName"] = GetProductName(archiveOld, content);
        }

        // Analyze folder structure for better naming
        ImproveAssetNaming(archiveOld);
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

    private static string GetProductName(LoadedArchiveOld archiveOld, string xmlContent)
    {
        try
        {
            var contentRoot = XDocument.Parse(xmlContent).Root!;
            return contentRoot.Element("ProductName")!.Attribute("VALUE")!.Value;
        }
        catch (Exception ex)
        {
            archiveOld.Status = ArchiveStatus.Error;
            archiveOld.Metadata["Error"] = ex.Message;
            return string.Empty;
        }
    }

    private static void ImproveAssetNaming(LoadedArchiveOld archiveOld)
    {
        // Try to improve asset naming based on folder structure
        var topLevelFolders = archiveOld.ContainedFiles
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
            if (folderName.Length > 3 && !folderName.Equals(archiveOld.Name, StringComparison.OrdinalIgnoreCase))
            {
                archiveOld.Metadata["SuggestedName"] = folderName;
            }
        }

        // Look for product name in folder structure
        var productFolders = archiveOld.ContainedFiles
            .Where(f => f.FileName.Contains("Product", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetDirectoryName(f.FileName))
            .FirstOrDefault(d => !string.IsNullOrEmpty(d));

        if (!string.IsNullOrEmpty(productFolders))
        {
            archiveOld.Metadata["ProductFolder"] = productFolders;
        }
    }
}