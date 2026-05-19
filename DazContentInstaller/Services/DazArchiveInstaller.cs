using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using SharpCompress.Readers;

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
        if (_workingDirectory.Exists)
            _workingDirectory.Delete(true);
    }

    public async IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(string libraryPath,
        IProgress<string>? messageProgress = null, IProgress<double>? percentProgress = null)
    {
        var resolvedLibraryPath = GetCanonicalPath(libraryPath);
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

            CopyDirectory(extractionDirectory, new DirectoryInfo(resolvedLibraryPath));
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
                var archiveBackupDirectory = Path.Combine(resolvedLibraryPath, "ArchiveBackup");
                Directory.CreateDirectory(archiveBackupDirectory);
                var archivePath = extractedArchiveLocation == _workingDirectory.FullName
                    ? loadedArchive.FilePath
                    : extractedArchiveLocation + Path.GetExtension(loadedArchive.FilePath);

                var archiveBackupPath = Path.Combine(archiveBackupDirectory, Path.GetFileName(archivePath));

                if (!PathsReferToSameLocation(archivePath, archiveBackupPath))
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
            currentDirectory = GetExtractionDirectory(extractedDirectory, loadedArchive.FilePath);
        }

        Directory.CreateDirectory(currentDirectory);

        await using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory) continue;
            await Task.Run(() => reader.WriteEntryToDirectory(currentDirectory));
        }
        return currentDirectory;
    }

    private static string GetExtractionDirectory(string parentDirectory, string archivePath)
    {
        var archiveDirectory = Path.GetDirectoryName(archivePath);
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);

        return string.IsNullOrEmpty(archiveDirectory)
            ? Path.Combine(parentDirectory, archiveName)
            : Path.Combine(parentDirectory, archiveDirectory, archiveName);
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

    private static bool PathsReferToSameLocation(string leftPath, string rightPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(GetCanonicalPath(leftPath), GetCanonicalPath(rightPath), comparison);
    }

    private static string GetCanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrEmpty(root))
            return Path.TrimEndingDirectorySeparator(fullPath);

        var segments = fullPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var resolvedPath = root;

        foreach (var segment in segments)
        {
            var nextPath = Path.Combine(resolvedPath, segment);
            var isDirectory = Directory.Exists(nextPath);
            var isFile = !isDirectory && File.Exists(nextPath);

            if (!isDirectory && !isFile)
            {
                resolvedPath = nextPath;
                continue;
            }

            FileSystemInfo fileSystemInfo = isDirectory
                ? new DirectoryInfo(nextPath)
                : new FileInfo(nextPath);

            resolvedPath = fileSystemInfo.ResolveLinkTarget(true)?.FullName ?? fileSystemInfo.FullName;
        }

        return Path.TrimEndingDirectorySeparator(resolvedPath);
    }
}