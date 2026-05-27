using System;
using System.IO;
using DazContentInstaller.Models;

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
        if (DirectoryInfo.Exists)
            DirectoryInfo.Delete(true);
    }
}

public interface IDirectoryService
{
    TempDirectory GetTempDirectory();
}

public class DirectoryService : IDirectoryService
{
    private readonly Func<AppSettings> _settingsProvider;

    public DirectoryService() : this(() => new AppSettings())
    {
    }

    public DirectoryService(AppSettings appSettings) : this(() => appSettings)
    {
    }

    public DirectoryService(SettingsService settingsService) : this(() => settingsService.CurrentSettings)
    {
    }

    private DirectoryService(Func<AppSettings> settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    public TempDirectory GetTempDirectory()
    {
        var appSettings = _settingsProvider();
        if (string.IsNullOrWhiteSpace(appSettings.TempWorkingDirectoryPath))
            return new TempDirectory(Directory.CreateTempSubdirectory("DazContentInstaller"));
        
        Directory.CreateDirectory(appSettings.TempWorkingDirectoryPath);
        var directory = Path.Combine(appSettings.TempWorkingDirectoryPath,
            $"DazContentInstaller-{Guid.NewGuid():N}");
        
        return new TempDirectory(Directory.CreateDirectory(directory));
    }
}