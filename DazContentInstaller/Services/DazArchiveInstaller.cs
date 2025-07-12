using System;
using System.IO;
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
        // using var archive = new SharpSevenZipExtractor(_loadedArchive.FilePath);
        // await archive.ExtractArchiveAsync(_tempDirectory.FullName);
    }

    public void Dispose()
    {
        _tempDirectory.Delete(true);
    }
}