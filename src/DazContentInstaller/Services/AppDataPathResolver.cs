using System;
using System.IO;

namespace DazContentInstaller.Services;

public static class AppDataPathResolver
{
    private const string AppFolderName = "daz-content-installer";

    public static string ResolveAppDataPath(string baseDirectory, bool isDevelopment)
    {
        if (!OperatingSystem.IsLinux() || isDevelopment)
            return baseDirectory;

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
            return Path.Combine(xdgDataHome, AppFolderName);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", AppFolderName);
    }
}