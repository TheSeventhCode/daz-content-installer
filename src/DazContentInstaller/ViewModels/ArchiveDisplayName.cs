using System;

namespace DazContentInstaller.ViewModels;

public static class ArchiveDisplayName
{
    public static string GetEffectiveDisplayName(string archiveName, string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName) ? archiveName : displayName;
    }

    public static bool HasDistinctDisplayName(string archiveName, string? displayName)
    {
        return !string.IsNullOrWhiteSpace(displayName)
               && !string.Equals(displayName, archiveName, StringComparison.OrdinalIgnoreCase);
    }
}
