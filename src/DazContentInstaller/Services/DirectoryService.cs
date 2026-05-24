using System;
using System.IO;

namespace DazContentInstaller.Services;

public class TempDirectory : IDisposable
{
    public DirectoryInfo DirectoryInfo { get; }

    public TempDirectory(DirectoryInfo directoryInfo)
    {
        DirectoryInfo = directoryInfo;
    }
    
    public void Dispose()
    {
        if(DirectoryInfo.Exists)
            DirectoryInfo.Delete(true);
    }
}

public interface IDirectoryService
{
    TempDirectory GetTempDirectory();
}

public class DirectoryService : IDirectoryService
{
    public TempDirectory GetTempDirectory()
    {
        return new TempDirectory(Directory.CreateTempSubdirectory("DazContentInstaller"));
    }
}