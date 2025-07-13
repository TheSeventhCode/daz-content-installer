using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DazContentInstaller.Models;
using SharpSevenZip;

namespace DazContentInstaller.Services;

public class DazArchiveInstaller : IDisposable
{
    private readonly LoadedArchive _loadedArchive;
    private readonly DirectoryInfo _tempDirectory;

    public DazArchiveInstaller(LoadedArchive loadedArchive)
    {
        _loadedArchive = loadedArchive;
        _tempDirectory = Directory.CreateTempSubdirectory("DazContentInstaller");
    }

    public async Task InstallAsync(string libraryPath, string existingPackageString, string? customBasePath = null,
        IProgress<string>? progress = null)
    {
        progress?.Report($"Installing {_loadedArchive.Name}...");

        var archivePath = Path.Combine(existingPackageString, Path.GetFileName(_loadedArchive.FilePath));
        var extractionDestinationPath =
            Path.Combine(existingPackageString, Path.GetFileNameWithoutExtension(_loadedArchive.FilePath));

        await ExtractArchiveAsync(archivePath, extractionDestinationPath);

        var extractionDirectory = new DirectoryInfo(string.IsNullOrEmpty(customBasePath)
            ? extractionDestinationPath
            : Path.Combine(extractionDestinationPath, customBasePath));
        var contentDirectory = extractionDirectory.GetDirectories()
            .FirstOrDefault(d => d.Name.Equals("content", StringComparison.OrdinalIgnoreCase));
        var archiveBaseDirectory = contentDirectory ?? extractionDirectory;

        CopyDirectory(archiveBaseDirectory, new DirectoryInfo(libraryPath));

        foreach (var archive in _loadedArchive.ContainedFiles)
        {
            archive.InstalledPath = !string.IsNullOrEmpty(customBasePath)
                ? archive.FileName.Split(customBasePath + Path.DirectorySeparatorChar).Last()
                : archive.FileName;
            
            archive.InstalledPath = archive.InstalledPath.StartsWith("content", StringComparison.OrdinalIgnoreCase)
                ? archive.InstalledPath[8..]
                : archive.InstalledPath;
        }
    }

    public void Dispose()
    {
        _tempDirectory.Delete(true);
    }

    private static async Task ExtractArchiveAsync(string archivePath, string destination)
    {
        using var archive = new SharpSevenZipExtractor(archivePath);
        await archive.ExtractArchiveAsync(destination);
    }

    private static void CopyDirectory(DirectoryInfo sourceDir, DirectoryInfo destinationDir)
    {
        var dirs = sourceDir.GetDirectories();

        Directory.CreateDirectory(destinationDir.FullName);

        foreach (var file in sourceDir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir.FullName, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dirs)
        {
            var newDestinationDir = new DirectoryInfo(Path.Combine(destinationDir.FullName, subDir.Name));
            CopyDirectory(subDir, newDestinationDir);
        }
    }
}