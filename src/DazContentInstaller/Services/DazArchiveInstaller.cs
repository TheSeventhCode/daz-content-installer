using System;
using System.Collections.Generic;
using System.IO;
using DazContentInstaller.Models;

namespace DazContentInstaller.Services;

public interface IDazArchiveInstaller
{
    IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(string libraryPath, IEnumerable<LoadedArchive> archives);
}

public class DazArchiveInstaller : IDazArchiveInstaller
{
    private readonly TempDirectory _workingDirectory;
    private readonly AppSettings _appSettings;

    public DazArchiveInstaller(AppSettings appSettings, IDirectoryService directoryService)
    {
        _appSettings = appSettings;
        _workingDirectory = directoryService.GetTempDirectory();
    }

    public async IAsyncEnumerable<LoadedArchive> InstallArchivesAsync(string libraryPath, IEnumerable<LoadedArchive> archives)
    {
        var resolvedLibraryPath = GetCanonicalPath(libraryPath);

        foreach (var archive in archives)
        {
            yield return archive;
        }
    }


    private static string GetCanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrEmpty(root))
            return Path.TrimEndingDirectorySeparator(fullPath);

        var segments = fullPath[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
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

            FileSystemInfo fileSystemInfo = isDirectory ? new DirectoryInfo(nextPath) : new FileInfo(nextPath);

            resolvedPath = fileSystemInfo.ResolveLinkTarget(true)?.FullName ?? fileSystemInfo.FullName;
        }

        return Path.TrimEndingDirectorySeparator(resolvedPath);
    }
}