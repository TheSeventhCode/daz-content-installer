using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

    public async IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(string libraryPath,
        IProgress<string>? messageProgress = null, IProgress<double>? percentProgress = null)
    {
        var index = 0;
        var increment = Math.Ceiling(100D / _loadedArchivesToInstall.Count());
        foreach (var loadedArchive in _loadedArchivesToInstall)
        {
            index++;
            loadedArchive.ArchiveStatus = ArchiveStatus.Installing;

            var extractedArchiveLocation = await FullExtractAsync(loadedArchive, _workingDirectory.FullName);
            var extractionDirectory = new DirectoryInfo(string.IsNullOrEmpty(loadedArchive.CustomAssetBaseDirectory)
                ? extractedArchiveLocation
                : Path.Combine(extractedArchiveLocation, loadedArchive.CustomAssetBaseDirectory));

            CopyDirectory(extractionDirectory, new DirectoryInfo(libraryPath));
            messageProgress?.Report($"Extracted {loadedArchive.Name}...");
            percentProgress?.Report(index * increment + increment / 3);
            
            foreach (var file in loadedArchive.ContainedFiles)
            {
                file.InstalledPath = !string.IsNullOrEmpty(loadedArchive.CustomAssetBaseDirectory)
                    ? file.FileName.Split(loadedArchive.CustomAssetBaseDirectory + Path.DirectorySeparatorChar).Last()
                    : file.FileName;

                file.InstalledPath = file.InstalledPath.StartsWith("content", StringComparison.OrdinalIgnoreCase)
                    ? file.InstalledPath[8..]
                    : file.InstalledPath;
            }

            messageProgress?.Report($"Installed {loadedArchive.Name}...");
            percentProgress?.Report(index * increment + 2 * increment / 3);
            
            if (_settings.CreateBackupBeforeInstall)
            {
                var archiveBackupPath = Path.Combine(libraryPath, "ArchiveBackup");
                Directory.CreateDirectory(archiveBackupPath);
                var archivePath = extractedArchiveLocation == _workingDirectory.FullName
                    ? loadedArchive.FilePath
                    : extractedArchiveLocation + Path.GetExtension(loadedArchive.FilePath);

                archiveBackupPath = Path.Combine(archiveBackupPath, Path.GetFileName(archivePath));

                File.Copy(archivePath, archiveBackupPath, true);
            }
            
            percentProgress?.Report(index * increment + increment);
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