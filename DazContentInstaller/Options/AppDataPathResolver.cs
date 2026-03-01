using System;
using System.IO;

namespace DazContentInstaller.Options;

public static class AppDataPathResolver
{
    private const string AppFolderName = "daz-content-installer";

    public static string ResolveAppDataPath(string baseDirectory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return baseDirectory;
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, AppFolderName);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".local", "share", AppFolderName);
    }

    public static void CopyLegacyDataIfNeeded(string legacyBaseDirectory, string appDataPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(appDataPath);

        CopyFileIfMissing(
            Path.Combine(legacyBaseDirectory, "database.db"),
            Path.Combine(appDataPath, "database.db"));

        CopyFileIfMissing(
            Path.Combine(legacyBaseDirectory, "settings.json"),
            Path.Combine(appDataPath, "settings.json"));

        CopyDirectoryIfMissing(
            Path.Combine(legacyBaseDirectory, "archives"),
            Path.Combine(appDataPath, "archives"));
    }

    private static void CopyFileIfMissing(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(destinationPath))
        {
            return;
        }

        File.Copy(sourcePath, destinationPath);
    }

    private static void CopyDirectoryIfMissing(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath) || Directory.Exists(destinationPath))
        {
            return;
        }

        CopyDirectoryRecursive(sourcePath, destinationPath);
    }

    private static void CopyDirectoryRecursive(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var filePath in Directory.GetFiles(sourcePath))
        {
            var destinationFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            File.Copy(filePath, destinationFilePath, overwrite: false);
        }

        foreach (var directoryPath in Directory.GetDirectories(sourcePath))
        {
            var destinationDirectoryPath = Path.Combine(destinationPath, Path.GetFileName(directoryPath));
            CopyDirectoryRecursive(directoryPath, destinationDirectoryPath);
        }
    }
}
