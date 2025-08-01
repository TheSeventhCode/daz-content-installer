﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;

namespace DazContentInstaller.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    public ObservableCollection<AssetLibraryModel> AssetLibraries { get; set; } = [];

    private readonly SettingsService _settingsService = null!;
    private readonly ApplicationDbContext _dbContext = null!;
    private bool _autoDetectDazLibraries = true;
    private bool _createBackupBeforeInstall = true;

    public bool AutoDetectDazLibraries
    {
        get => _autoDetectDazLibraries;
        set
        {
            _settingsService.CurrentSettings.AutoDetectDazLibraries = value;
            SetProperty(ref _autoDetectDazLibraries, value);
        }
    }

    public bool CreateBackupBeforeInstall
    {
        get => _createBackupBeforeInstall;
        set
        {
            _settingsService.CurrentSettings.CreateBackupBeforeInstall = value;
            SetProperty(ref _createBackupBeforeInstall, value);
        }
    }
    
    public SettingsWindowViewModel(SettingsService settingsService,
        ApplicationDbContext dbContext) : this()
    {
        _settingsService = settingsService;
        _dbContext = dbContext;
    }

    public SettingsWindowViewModel()
    {
        AutoDetectClick = ReactiveCommand.CreateFromTask(AutoDetectLibrariesAsync);
        RemoveLibraryClick = ReactiveCommand.Create<AssetLibraryModel>(RemoveLibrary);
    }

    public ReactiveCommand<Unit, Unit> AutoDetectClick { get; set; }
    public ReactiveCommand<AssetLibraryModel, Unit> RemoveLibraryClick { get; set; }

    private async Task AutoDetectLibrariesAsync()
    {
        await _settingsService.AutoDetectDazLibrariesAsync();
        await ReloadLibrariesAsync();
    }

    private void RemoveLibrary(AssetLibraryModel libraryModel)
    {
        AssetLibraries.Remove(libraryModel);
        if (!libraryModel.IsDefault) return;

        var nextDefault = AssetLibraries.OrderByDescending(l => l.CreatedDate).FirstOrDefault();
        if (nextDefault is not null)
            nextDefault.IsDefault = true;
    }

    public async Task LoadSettingsAsync()
    {
        await _settingsService.LoadSettingsAsync();
        AutoDetectDazLibraries = _settingsService.CurrentSettings.AutoDetectDazLibraries;
        CreateBackupBeforeInstall = _settingsService.CurrentSettings.CreateBackupBeforeInstall;

        await ReloadLibrariesAsync();
    }

    public async Task SaveAsync()
    {
        _settingsService.CurrentSettings.AutoDetectDazLibraries = AutoDetectDazLibraries;
        _settingsService.CurrentSettings.CreateBackupBeforeInstall = CreateBackupBeforeInstall;

        await _settingsService.SaveSettingsAsync();

        var dbLibraries = await _dbContext.AssetLibraries.ToListAsync();
        var newLibraries = AssetLibraries.Where(l => dbLibraries.All(dl => dl.Id != l.Id)).ToList();

        foreach (var assetLibraryModel in newLibraries)
        {
            var library = new AssetLibrary
            {
                Id = assetLibraryModel.Id,
                Name = assetLibraryModel.Name,
                Path = assetLibraryModel.Path,
                IsDefault = assetLibraryModel.IsDefault,
                CreatedDate = assetLibraryModel.CreatedDate
            };
            _dbContext.AssetLibraries.Add(library);
        }

        var removedLibraries = dbLibraries.Where(l => AssetLibraries.All(al => al.Id != l.Id)).ToList();
        foreach (var library in removedLibraries) _dbContext.AssetLibraries.Remove(library);

        var updatedLibraries = AssetLibraries.Where(l => dbLibraries.Any(dl => dl.Id == l.Id)).ToList();
        foreach (var library in updatedLibraries)
        {
            var dbLibrary = dbLibraries.First(dl => dl.Id == library.Id);
            dbLibrary.Name = library.Name;
            dbLibrary.Path = library.Path;
            dbLibrary.IsDefault = library.IsDefault;
        }

        await _dbContext.SaveChangesAsync();
    }

    public void AddLibrary(IStorageFolder folder)
    {
        var library = new AssetLibraryModel(Guid.CreateVersion7(), folder.Name, folder.Path.LocalPath, false,
            DateTime.Now);
        AssetLibraries.Add(library);
    }

    public async Task ReloadLibrariesAsync()
    {
        AssetLibraries.Clear();

        var libraries = await _dbContext.AssetLibraries
            .OrderByDescending(l => l.CreatedDate)
            .Select(l => new AssetLibraryModel(l.Id, l.Name, l.Path, l.IsDefault, l.CreatedDate))
            .ToListAsync();

        foreach (var item in libraries)
            AssetLibraries.Add(item);
    }
}