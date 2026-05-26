namespace DazContentInstaller.Database;

public static class ArchiveInstallEligibility
{
    public static bool BlocksDuplicateInstall(ArchiveStatus status) =>
        status == ArchiveStatus.Installed;

    public static bool IsResumable(ArchiveStatus status) =>
        status is ArchiveStatus.Loading or ArchiveStatus.Installing or ArchiveStatus.Error;
}
