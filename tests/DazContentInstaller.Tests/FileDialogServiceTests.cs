using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class FileDialogServiceTests
{
    [Fact]
    public async Task MethodsReturnEmptyResultsWhenStorageProviderUnavailable()
    {
        var service = new FileDialogService(() => null);

        (await service.OpenArchiveFilesAsync()).ShouldBeEmpty();
        (await service.OpenFilesAsync("Select files")).ShouldBeEmpty();
        (await service.OpenFolderAsync("Select folder")).ShouldBeNull();
    }
}