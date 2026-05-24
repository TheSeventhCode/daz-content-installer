using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DirectoryServiceTests
{
    [Fact]
    public void GetTempDirectory_ReturnsValidTempDirectory()
    {
        var service = new DirectoryService();
        
        var tempDirectory = service.GetTempDirectory();
        
        tempDirectory.DirectoryInfo.Exists.ShouldBeTrue();
    }

    [Fact]
    public void GetTempDirectory_TempDirectoryDispose_CleansUpTemporaryDirectory()
    {
        var service = new DirectoryService();
        
        var tempDirectory = service.GetTempDirectory();
        
        tempDirectory.DirectoryInfo.Exists.ShouldBeTrue();
        
        tempDirectory.Dispose();
        
        tempDirectory.DirectoryInfo.Exists.ShouldBeFalse();
    }
}