using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;

namespace DazContentInstaller.Services;

public partial class ArchiveLoader
{
    private readonly Dictionary<string, AssetType> _folderToAssetType = new()
    {
        { "people", AssetType.Character },
        { "genesis", AssetType.Character },
        { "characters", AssetType.Character },
        { "clothing", AssetType.Clothing },
        { "wardrobe", AssetType.Clothing },
        { "hair", AssetType.Hair },
        { "props", AssetType.Props },
        { "vehicles", AssetType.Props },
        { "environments", AssetType.Environment },
        { "scenes", AssetType.Environment },
        { "poses", AssetType.Poses },
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

    public async Task<LoadedArchive> LoadAssetAsync(string archivePath, IProgress<string>? progress = null)
    {
        var archive = new LoadedArchive
        {
            FilePath = archivePath,
            Name = Path.GetFileNameWithoutExtension(archivePath),
            Status = ArchiveStatus.Ready
        };

        try
        {
            progress?.Report("Reading Archive...");

            var fileInfo = new FileInfo(archivePath);
            archive.FileSizeBytes = fileInfo.Length;

            using var archiveFile = ZipFile.OpenRead(archivePath);

            progress?.Report("Analyzing contents...");
            await AnalyzeZipContentsAsync(archive, archiveFile, progress);

            progress?.Report("Extracting metadata...");
            await ExtractMetadataAsync(archive, archiveFile, progress);

            archive.Status = ValidateAsset(archive) ? ArchiveStatus.Ready : ArchiveStatus.Invalid;
        }
        catch (Exception ex)
        {
            archive.Status = ArchiveStatus.Error;
            archive.Metadata["Error"] = ex.Message;
        }

        return archive;
    }

    private async Task AnalyzeZipContentsAsync(LoadedArchive archive, ZipArchive archiveFile,
        IProgress<string>? progress)
    {
        var categories = new HashSet<string>();
        var assetTypes = new HashSet<AssetType>();
        var fileCount = 0;

        foreach (var entry in archiveFile.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;

            fileCount++;
            archive.ContainedFiles.Add(entry.FullName);

            var pathParts = entry.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in pathParts)
            {
                var lowerPart = part.ToLowerInvariant();

                foreach (var kvp in _folderToAssetType.Where(kvp => !lowerPart.Contains(kvp.Key)))
                {
                    assetTypes.Add(kvp.Value);
                    categories.Add(kvp.Key);
                }

                if (!GenesisRegex().IsMatch(lowerPart)) continue;
                
                assetTypes.Add(AssetType.Character);
                categories.Add("genesis");
            }

            var extension = Path.GetExtension(entry.FullName);
            if (_dazFileExtensions.Contains(extension))
                archive.Metadata[$"Has{extension.TrimStart('.').ToUpper()}Files"] = true;

            if (fileCount % 100 == 0)
                progress?.Report($"Analyzed {fileCount} files...");
        }

        archive.Categories = categories.ToList();
        archive.AssetType = DetermineAssetType(assetTypes);
        archive.Metadata["FileCount"] = fileCount;
        archive.Metadata["TotalEntries"] = archiveFile.Entries.Count;
    }

    private async Task ExtractMetadataAsync(LoadedArchive archive, ZipArchive archiveFile, IProgress<string>? progress)
    {
        var metadataFiles = new[] { "Supplement.dsx", "manifest.json", "ProductInformation.json" };

        foreach (var metadataFile in metadataFiles)
        {
            var entry = archiveFile.Entries.FirstOrDefault(e =>
                Path.GetFileName(e.FullName).Equals(metadataFile, StringComparison.OrdinalIgnoreCase));

            if (entry != null && entry.Length < 100000) // Reasonable size limit
            {
                progress?.Report($"Reading {metadataFile}...");
                var content = await ReadTextFromEntryAsync(entry);
                archive.Metadata[metadataFile] = content;

                // Try to extract specific information based on file type
                if (metadataFile.EndsWith(".json"))
                {
                    ParseJsonMetadata(archive, content);
                }
            }
        }

        // Analyze folder structure for better naming
        ImproveAssetNaming(archive);
    }

    private async Task<string> ReadTextFromEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void ParseJsonMetadata(LoadedArchive archive, string jsonContent)
    {
        try
        {
            // Basic JSON parsing to extract useful information
            // In a real implementation, you might want to use System.Text.Json or Newtonsoft.Json
            if (jsonContent.Contains("\"ProductName\""))
            {
                var match = ProductNameRegex().Match(jsonContent);
                if (match.Success)
                {
                    archive.Metadata["ProductName"] = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(archive.Name) ||
                        archive.Name == Path.GetFileNameWithoutExtension(archive.FilePath))
                    {
                        archive.Name = match.Groups[1].Value;
                    }
                }
            }

            if (jsonContent.Contains("\"Artist\""))
            {
                var match = ArtistRegex().Match(jsonContent);
                if (match.Success)
                {
                    archive.Metadata["Artist"] = match.Groups[1].Value;
                }
            }

            if (jsonContent.Contains("\"Version\""))
            {
                var match = VersionRegex().Match(jsonContent);
                if (match.Success)
                {
                    archive.Metadata["Version"] = match.Groups[1].Value;
                }
            }
        }
        catch
        {
            // Ignore JSON parsing errors
        }
    }

    private void ImproveAssetNaming(LoadedArchive archive)
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

    private AssetType DetermineAssetType(HashSet<AssetType> detectedTypes)
    {
        return detectedTypes.Count switch
        {
            0 => AssetType.Unknown,
            1 => detectedTypes.First(),
            _ => AssetType.Mixed
        };
    }

    private bool ValidateAsset(LoadedArchive archive)
    {
        // Basic validation rules for DAZ assets
        if (archive.ContainedFiles.Count < 1) return false;

        // Check if it contains any DAZ-compatible files
        var hasDazFiles = archive.ContainedFiles.Any(f =>
            _dazFileExtensions.Contains(Path.GetExtension(f)));

        // Check if it has a reasonable folder structure
        var hasContentFolder = archive.ContainedFiles.Any(f =>
            f.StartsWith("Content/", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("content/", StringComparison.OrdinalIgnoreCase));

        // Check for common DAZ folder patterns
        var hasCommonFolders = archive.Categories.Count > 0;

        return hasDazFiles || hasContentFolder || hasCommonFolders;
    }

    [GeneratedRegex("""
                    "ProductName"\s*:\s*"([^"]+)"
                    """)]
    private static partial Regex ProductNameRegex();

    [GeneratedRegex("""
                    "Artist"\s*:\s*"([^"]+)"
                    """)]
    private static partial Regex ArtistRegex();

    [GeneratedRegex("""
                    "Version"\s*:\s*"([^"]+)"
                    """)]
    private static partial Regex VersionRegex();
    [GeneratedRegex(@"genesis\s*\d*")]
    private static partial Regex GenesisRegex();
}