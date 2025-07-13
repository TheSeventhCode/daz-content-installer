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

    public async Task InstallAsync(string libraryPath)
    {
        var archivePath = _loadedArchive.FilePath;
        var extractionDestinationPath = _tempDirectory.FullName;
        if (_loadedArchive.IsPartOfParentArchive)
        {
            var parentArchivePath = Path.GetDirectoryName(_loadedArchive.FilePath)!;
            await ExtractArchiveAsync(parentArchivePath, _tempDirectory.FullName);
            archivePath = Path.Combine(extractionDestinationPath, Path.GetFileName(_loadedArchive.FilePath));
            extractionDestinationPath = Path.Combine(extractionDestinationPath, Path.GetFileNameWithoutExtension(_loadedArchive.FilePath));
        }
        
        await ExtractArchiveAsync(archivePath, extractionDestinationPath);
        
        var extractionDirectory = new DirectoryInfo(extractionDestinationPath);
        var contentDirectory = extractionDirectory.GetDirectories()
            .FirstOrDefault(d => d.Name.Equals("content", StringComparison.OrdinalIgnoreCase));
        var archiveBaseDirectory = contentDirectory ?? extractionDirectory;
        
        CopyDirectory(archiveBaseDirectory, new DirectoryInfo(libraryPath));
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
            var targetFilePath =  Path.Combine(destinationDir.FullName, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dirs)
        {
            var newDestinationDir = new DirectoryInfo(Path.Combine(destinationDir.FullName, subDir.Name));
            CopyDirectory(subDir, newDestinationDir);
        }
    }
}