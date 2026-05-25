using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DazContentInstaller.Services;

public class SettingsService(
    IOptions<InstallerConfig> options,
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = options.Value.AppSettingsPath;
    private readonly InstallerConfig _config = options.Value;
    private bool _settingsLoaded;

    public AppSettings CurrentSettings { get; private set; } = new();

    public async Task EnsureSettingsLoadedAsync()
    {
        if (_settingsLoaded)
            return;

        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                await AutoDetectDazLibrariesAsync();
                ApplyDefaults();
                await SaveSettingsAsync();
                _settingsLoaded = true;
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (settings is not null)
                CurrentSettings = settings;

            ApplyDefaults();
            _settingsLoaded = true;
        }
        catch (Exception e)
        {
            Console.WriteLine("Error loading settings: {0}", e);
            CurrentSettings = new AppSettings();
            ApplyDefaults();
            _settingsLoaded = true;
        }
    }

    /// <summary>
    /// Saves the <see cref="CurrentSettings"/> to the defined <see cref="_settingsPath"/>
    /// </summary>
    /// <exception cref="InvalidOperationException">The settings could not be saved.</exception>
    public async Task SaveSettingsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(CurrentSettings, JsonSerializerOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Failed to save settings: {0}", e);
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        CurrentSettings = settings;
        ApplyDefaults();
        _settingsLoaded = true;
    }

    public async Task AutoDetectDazLibrariesAsync()
    {
        var potentialPaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DAZ 3D", "Studio",
                "My Library"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DAZ 3D", "Studio",
                "My DAZ 3D Library"),
            @"C:\Users\Public\Documents\My DAZ 3D Library"
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var currentLibraries = await dbContext.AssetLibraries.Select(d => d.Path).ToListAsync();

        foreach (var library in potentialPaths
                     .Except(currentLibraries)
                     .Where(Directory.Exists)
                     .Select(path =>
                         new AssetLibrary { Name = Path.GetFileName(path), Path = path }))
        {
            dbContext.AssetLibraries.Add(library);
        }

        await dbContext.SaveChangesAsync();
    }

    private void ApplyDefaults()
    {
        CurrentSettings.DefaultArchiveBackupPath ??= _config.ArchiveBackupPath;
    }
}