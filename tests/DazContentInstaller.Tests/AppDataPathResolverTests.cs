using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class AppDataPathResolverTests
{
    [Fact]
    public void ResolveAppDataPath_ReturnsBaseDirectoryWhenDevelopment()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "daz-dev-base");

        AppDataPathResolver.ResolveAppDataPath(baseDirectory, isDevelopment: true)
            .ShouldBe(baseDirectory);
    }

    [Fact]
    public void ResolveAppDataPath_UsesXdgDataHomeOnLinuxWhenNotDevelopment()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var original = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var xdgDataHome = Path.Combine(Path.GetTempPath(), $"xdg-test-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", xdgDataHome);

            AppDataPathResolver.ResolveAppDataPath("/ignored-base", isDevelopment: false)
                .ShouldBe(Path.Combine(xdgDataHome, "daz-content-installer"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", original);
        }
    }

    [Fact]
    public void ResolveAppDataPath_UsesLocalShareWhenXdgDataHomeMissingOnLinux()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var original = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AppDataPathResolver.ResolveAppDataPath("/ignored-base", isDevelopment: false)
                .ShouldBe(Path.Combine(home, ".local", "share", "daz-content-installer"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", original);
        }
    }
}