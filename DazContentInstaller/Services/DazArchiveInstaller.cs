using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using SharpSevenZip;

namespace DazContentInstaller.Services;

public class DazArchiveInstaller : IDisposable
{
    private readonly DirectoryInfo _workingDirectory;
    private readonly IEnumerable<LoadedArchive> _loadedArchivesToInstall;
    private readonly AppSettings _settings;

    public DazArchiveInstaller(IEnumerable<LoadedArchive> archivesToInstall, AppSettings settings)
    {
        _workingDirectory = Directory.CreateTempSubdirectory("DazInstaller");
        _loadedArchivesToInstall = archivesToInstall;
        _settings = settings;
    }

    public void Dispose()
    {
        _workingDirectory.Delete(true);
    }

    public async IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(string libraryPath, IProgress<string>? progress = null)
    {
        foreach (var loadedArchive in _loadedArchivesToInstall)
        {
            progress?.Report($"Installing {loadedArchive.Name}...");
            loadedArchive.ArchiveStatus = ArchiveStatus.Installing;

            var extractedArchiveLocation = await FullExtractAsync(loadedArchive, _workingDirectory.FullName);
            var extractionDirectory = new DirectoryInfo(string.IsNullOrEmpty(loadedArchive.CustomAssetBaseDirectory)
                ? extractedArchiveLocation
                : Path.Combine(extractedArchiveLocation, loadedArchive.CustomAssetBaseDirectory));

            CopyDirectory(extractionDirectory, new DirectoryInfo(libraryPath));

            foreach (var file in loadedArchive.ContainedFiles)
            {
                file.InstalledPath = !string.IsNullOrEmpty(loadedArchive.CustomAssetBaseDirectory)
                    ? file.FileName.Split(loadedArchive.CustomAssetBaseDirectory + Path.DirectorySeparatorChar).Last()
                    : file.FileName;
            
                file.InstalledPath = file.InstalledPath.StartsWith("content", StringComparison.OrdinalIgnoreCase)
                    ? file.InstalledPath[8..]
                    : file.InstalledPath;
            }

            if (!_settings.CreateBackupBeforeInstall) continue;
            
            var archiveBackupPath = Path.Combine(libraryPath, "ArchiveBackup");
            Directory.CreateDirectory(archiveBackupPath);
            var archivePath = extractedArchiveLocation + Path.GetExtension(loadedArchive.FilePath);
            
            archiveBackupPath = Path.Combine(archiveBackupPath, Path.GetFileName(archivePath));
            
            File.Copy(archivePath, archiveBackupPath, true);

            loadedArchive.ArchiveStatus = ArchiveStatus.Installed;
            yield return loadedArchive;
        }
    }

    private static async Task<string> FullExtractAsync(LoadedArchive loadedArchive, string currentDirectory)
    {
        var archivePath = loadedArchive.FilePath;
        if (loadedArchive.ParentArchive is not null)
        {
            var extractedDirectory = await FullExtractAsync(loadedArchive.ParentArchive, currentDirectory);
            archivePath = Path.Combine(extractedDirectory, loadedArchive.FilePath);
            currentDirectory = Path.Combine(extractedDirectory,
                loadedArchive.FilePath.Split(Path.GetExtension(loadedArchive.FilePath))[0]);
        }

        using var archive = new SharpSevenZipExtractor(archivePath);
        await archive.ExtractArchiveAsync(currentDirectory);
        return currentDirectory;
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