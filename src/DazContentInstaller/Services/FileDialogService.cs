using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace DazContentInstaller.Services;

public interface IFileDialogService
{
    Task<IReadOnlyList<string>> OpenArchiveFilesAsync();
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, bool allowMultiple = true);
    Task<string?> OpenFolderAsync(string title, string? startPath = null);
}

public class FileDialogService(Func<IStorageProvider?>? storageProviderFactory = null) : IFileDialogService
{
    private readonly Func<IStorageProvider?> _storageProviderFactory =
        storageProviderFactory ?? (GetDefaultStorageProvider);

    public async Task<IReadOnlyList<string>> OpenArchiveFilesAsync()
    {
        var storageProvider = _storageProviderFactory();
        if (storageProvider is null)
            return [];

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select archive files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Archives")
                {
                    Patterns = ["*.zip", "*.7z", "*.rar", "*.tar", "*.gz", "*.tgz", "*.bz2", "*.xz"]
                },
                FilePickerFileTypes.All
            ]
        });

        return files
            .Select(x => x.Path.LocalPath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, bool allowMultiple = true)
    {
        var storageProvider = _storageProviderFactory();
        if (storageProvider is null)
            return [];

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = [FilePickerFileTypes.All]
        });

        return files
            .Select(x => x.Path.LocalPath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();
    }

    public async Task<string?> OpenFolderAsync(string title, string? startPath = null)
    {
        var storageProvider = _storageProviderFactory();
        if (storageProvider is null)
            return null;

        IStorageFolder? startLocation = null;
        if (!string.IsNullOrWhiteSpace(startPath))
            startLocation = await storageProvider.TryGetFolderFromPathAsync(startPath);

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = startLocation
        });

        return folders.ElementAtOrDefault(0)?.Path.LocalPath;
    }

    private static IStorageProvider? GetDefaultStorageProvider()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?.StorageProvider;
    }
}