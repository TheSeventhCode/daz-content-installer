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
}