using DazContentInstaller.Services;
using DazContentInstaller.Models;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DirectoryServiceTests
{
    [Fact]
    public void GetTempDirectory_ReturnsValidTempDirectory()
    {
        var service = new DirectoryService(new AppSettings());

        var tempDirectory = service.GetTempDirectory();

        tempDirectory.DirectoryInfo.Exists.ShouldBeTrue();
    }

    [Fact]
    public void GetTempDirectory_TempDirectoryDispose_CleansUpTemporaryDirectory()
    {
        var service = new DirectoryService(new AppSettings());

        var tempDirectory = service.GetTempDirectory();

        tempDirectory.DirectoryInfo.Exists.ShouldBeTrue();

        tempDirectory.Dispose();

        tempDirectory.DirectoryInfo.Exists.ShouldBeFalse();
    }

    [Fact]
    public void GetTempDirectory_UsesConfiguredTempWorkingDirectoryPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"DazContentInstallerCustomTemp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var service = new DirectoryService(new AppSettings { TempWorkingDirectoryPath = tempRoot });

            using var tempDirectory = service.GetTempDirectory();

            tempDirectory.DirectoryInfo.FullName.ShouldStartWith(tempRoot);
            tempDirectory.DirectoryInfo.Exists.ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }
}