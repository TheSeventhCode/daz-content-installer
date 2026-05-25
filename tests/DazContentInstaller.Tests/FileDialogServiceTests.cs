using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class FileDialogServiceTests
{
    [Fact]
    public async Task OpenArchiveFilesAsync_ReturnsEmptyWhenStorageProviderUnavailable()
    {
        var service = new FileDialogService(() => null);

        var result = await service.OpenArchiveFilesAsync();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenFolderAsync_ReturnsNullWhenStorageProviderUnavailable()
    {
        var service = new FileDialogService(() => null);

        var result = await service.OpenFolderAsync("Select folder");

        result.ShouldBeNull();
    }
}