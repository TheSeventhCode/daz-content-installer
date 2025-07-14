using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DazContentInstaller.Database;

namespace DazContentInstaller.Services;

public class DazArchiveUninstaller
{
    private readonly DirectoryInfo _libraryPath;
    private readonly Archive _archive;

    public DazArchiveUninstaller(Archive archive)
    {
        _archive = archive;
        _libraryPath = new DirectoryInfo(_archive.AssetLibrary.Path);
    }

    public async Task UninstallArchiveAsync(HashSet<string> deleteFileExceptions)
    {
        foreach (var file in _archive.AssetFiles)
        {
            var fileInfo = new FileInfo(Path.Combine(_archive.AssetLibrary.Path, file.InstalledPath!));
            if (!fileInfo.Exists)
                continue;

            try
            {
                if(!deleteFileExceptions.Contains(file.InstalledPath!))
                    fileInfo.Delete();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }

            DeleteEmptyDirectory(fileInfo.Directory!);

            await Task.Yield();
        }
    }

    private void DeleteEmptyDirectory(DirectoryInfo directory)
    {
        while (true)
        {
            if (directory?.GetFileSystemInfos().Length < 1)
            {
                try
                {
                    directory.Delete();
                    if (directory.Parent is not null &&
                        !directory.Parent.FullName.Equals(_libraryPath.FullName.TrimEnd(Path.DirectorySeparatorChar),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        directory = directory.Parent;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
            }

            break;
        }
    }
}