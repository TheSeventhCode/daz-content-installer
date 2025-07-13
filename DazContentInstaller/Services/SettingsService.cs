using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DazContentInstaller.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly ApplicationDbContext _dbContext;

    public SettingsService(IOptions<InstallerConfig> config, ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
        _settingsPath = config.Value.AppSettingsPath;
        CurrentSettings = new AppSettings();
    }

    public AppSettings CurrentSettings { get; private set; }

    public async Task LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    CurrentSettings = settings;
                }
            }
            else
            {
                // First run - try to auto-detect DAZ libraries
                await AutoDetectDazLibrariesAsync();
            }
        }
        catch (Exception ex)
        {
            // Log error and use default settings
            Console.WriteLine($"Error loading settings: {ex.Message}");
            CurrentSettings = new AppSettings();
        }
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save settings: {ex.Message}", ex);
        }
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

        var currentLibraries = await _dbContext.AssetLibraries.Select(d => d.Path).ToListAsync();

        var makeDefault = currentLibraries.Count < 1;
        foreach (var library in potentialPaths
                     .Except(currentLibraries)
                     .Where(Directory.Exists)
                     .Select(path =>
                         new AssetLibrary { Name = Path.GetFileName(path), Path = path, IsDefault = makeDefault }))
        {
            makeDefault = false;
            _dbContext.AssetLibraries.Add(library);
        }

        await _dbContext.SaveChangesAsync();
    }
}