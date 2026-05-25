using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class InstallerConfigTests
{
    [Fact]
    public void DerivedPaths_AreBasedOnAppDataPath()
    {
        var config = new InstallerConfig { AppDataPath = "/tmp/daz-appdata" };

        config.DbPath.ShouldBe(Path.Combine("/tmp/daz-appdata", "database.db"));
        config.ArchiveBackupPath.ShouldBe(Path.Combine("/tmp/daz-appdata", "archives"));
        config.AppSettingsPath.ShouldBe(Path.Combine("/tmp/daz-appdata", "settings.json"));
    }
}