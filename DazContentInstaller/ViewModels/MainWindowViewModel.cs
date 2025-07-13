using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using DynamicData;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;

namespace DazContentInstaller.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory = null!;
    public ObservableCollection<LoadedArchive> LoadedArchives { get; set; } = [];
    private InstalledArchiveTree InstalledArchivesTree { get; } = [];
    public ObservableCollection<TreeNode> DisplayedInstalledArchives { get; } = [];

    private ObservableCollection<LoadedArchive> _selectedArchives = [];

    public ObservableCollection<LoadedArchive> SelectedArchives
    {
        get => _selectedArchives;
        set => SetProperty(ref _selectedArchives, value);
    }

    public TreeNode? SelectedInstallNode
    {
        get => _selectedInstallNode;
        set
        {
            SetProperty(ref _selectedInstallNode, value);
            OnPropertyChanged(nameof(UninstallButtonEnabled));
        }
    }

    public ObservableCollection<AssetLibrary> AssetLibraries { get; set; } = [];
    private AssetLibrary? _currentSelectedAssetLibrary;

    public AssetLibrary? CurrentSelectedAssetLibrary
    {
        get => _currentSelectedAssetLibrary;
        set => SetProperty(ref _currentSelectedAssetLibrary, value);
    }

    private bool _installButtonEnabled;

    public bool InstallButtonEnabled
    {
        get => _installButtonEnabled;
        set => SetProperty(ref _installButtonEnabled, value);
    }

    public bool UninstallButtonEnabled => SelectedInstallNode is not null && SelectedInstallNode.Parent is null;

    private string _statusText = "Ready";
    private TreeNode? _selectedInstallNode;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public MainWindowViewModel()
    {
        ClearLoadedArchivesCommand = ReactiveCommand.Create(ClearLoadedArchives);
        InstallArchivesCommand = ReactiveCommand.CreateFromTask(InstallArchives);
        UninstallArchiveCommand = ReactiveCommand.CreateFromTask(UninstallArchiveAsync);
        RefreshInstalledAssets = ReactiveCommand.CreateFromTask(LoadInstalledArchivesAsync);
    }

    public MainWindowViewModel(IDbContextFactory<ApplicationDbContext> dbContextFactory) : this()
    {
        _dbContextFactory = dbContextFactory;
    }

    private void ClearLoadedArchives()
    {
        LoadedArchives.Clear();
        UpdateInstallButton();
        StatusText = "Ready";
    }

    public ReactiveCommand<Unit, Unit> ClearLoadedArchivesCommand { get; set; }
    public ReactiveCommand<Unit, Unit> RefreshInstalledAssets { get; set; }
    public ReactiveCommand<Unit, Unit> InstallArchivesCommand { get; set; }
    public ReactiveCommand<Unit, Unit> UninstallArchiveCommand { get; set; }

    public async Task LoadAssetLibrariesAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var libraries = await dbContext.AssetLibraries.ToListAsync();
        AssetLibraries.Clear();
        AssetLibraries.AddRange(libraries);
        CurrentSelectedAssetLibrary = libraries.OrderByDescending(d => d.IsDefault).FirstOrDefault();
    }

    public async Task LoadInstalledArchivesAsync()
    {
        InstalledArchivesTree.Clear();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var archives = await dbContext.Archives
            .Where(d => d.Status == ArchiveStatus.Installed)
            .Include(d => d.AssetFiles)
            .OrderBy(d => d.ArchiveName)
            .ToListAsync();

        foreach (var archive in archives)
            InstalledArchivesTree.LoadArchive(archive);

        FilterInstalledAssetsTree(string.Empty);
    }

    private async Task InstallArchives()
    {
        if (CurrentSelectedAssetLibrary is null)
            return;

        var archivesToInstall =
            SelectedArchives.Count > 0 ? SelectedArchives.ToList() : LoadedArchives.ToList();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        foreach (var archive in archivesToInstall)
        {
            var dbArchive = new Archive
            {
                ArchiveName = archive.Name,
                ArchiveSize = archive.FileSizeBytes,
                Status = archive.Status,
                AssetLibraryId = CurrentSelectedAssetLibrary.Id
            };

            dbArchive.AssetFiles.AddRange(archive.ContainedFiles);
            dbContext.Archives.Add(dbArchive);
            await dbContext.SaveChangesAsync();

            using var installer = new DazArchiveInstaller(archive);
            await installer.InstallAsync(CurrentSelectedAssetLibrary.Path);

            dbArchive.Status = ArchiveStatus.Installed;
            await dbContext.SaveChangesAsync();

            LoadedArchives.Remove(archive);
        }

        await LoadInstalledArchivesAsync();
    }

    private async Task UninstallArchiveAsync()
    {
        if (SelectedInstallNode is null)
            return;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var archive = await dbContext.Archives
            .Include(a => a.AssetLibrary)
            .Include(a => a.AssetFiles)
            .FirstOrDefaultAsync(a => a.Id == SelectedInstallNode.DbId);
        if (archive is null)
            return;

        var deleteFileExceptions = await dbContext.AssetFiles
            .Where(f => f.ArchiveId != archive.Id && f.InstalledPath != null)
            .ToListAsync();

        deleteFileExceptions = deleteFileExceptions
            .Where(f => archive.AssetFiles.Any(a =>
                a.InstalledPath!.Equals(f.InstalledPath, StringComparison.OrdinalIgnoreCase))).Distinct().ToList();

        var uninstaller = new DazArchiveUninstaller(archive);
        uninstaller.UninstallArchive(deleteFileExceptions.Select(d => d.InstalledPath!).ToHashSet());

        dbContext.Archives.Remove(archive);
        await dbContext.SaveChangesAsync();
        await dbContext.SaveChangesAsync();

        await LoadInstalledArchivesAsync();
    }

    public async Task LoadArchiveFileAsync(string filePath)
    {
        try
        {
            using var archive = new DazArchiveLoader(filePath);
            var result = await archive.LoadArchiveAsync();

            LoadedArchives.Add(result);
            UpdateInstallButton();
            StatusText = $"Loaded {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading {Path.GetFileName(filePath)}: {ex.Message}";
        }
    }

    public void FilterInstalledAssetsTree(string? searchTerm)
    {
        DisplayedInstalledArchives.Clear();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            DisplayedInstalledArchives.AddRange(InstalledArchivesTree);
            return;
        }

        var filtered = InstalledArchivesTree
            .Select(node => FilterTree(node, searchTerm, node.Parent))
            .Where(node => node is not null)
            .Select(node => node!);

        DisplayedInstalledArchives.AddRange(filtered);
    }

    private static TreeNode? FilterTree(TreeNode node, string searchTerm, TreeNode? parent)
    {
        // Filter children recursively
        var filteredChildren = node.Children
            .Select(child => FilterTree(child, searchTerm, child.Parent))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToList();

        // Check if this node matches or has any matching children
        var isMatch = node.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

        if (isMatch || filteredChildren.Count > 0)
            return new TreeNode(node.Title, node.DbId, filteredChildren, node.Parent);

        return null;
    }

    private void UpdateInstallButton()
    {
        InstallButtonEnabled = LoadedArchives.Count > 0 && CurrentSelectedAssetLibrary != null;
    }
}