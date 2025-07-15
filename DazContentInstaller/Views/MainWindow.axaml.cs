using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DazContentInstaller.Models;
using DazContentInstaller.Services;
using DazContentInstaller.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using SharpSevenZip;

namespace DazContentInstaller.Views;

public partial class MainWindow : Window
{
    private readonly string[] _allowedExtensions = [".zip", ".rar", ".7z"];

    private MainWindowViewModel ViewModel =>
        DataContext as MainWindowViewModel ?? throw new InvalidOperationException();

    public MainWindow()
    {
        InitializeComponent();

        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);

        BrowseButton.Click += OnBrowseClick;
    }

    private async Task OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            DropZone.Background = Avalonia.Media.Brushes.Transparent;

            if (!e.Data.Contains(DataFormats.Files)) return;

            var files = e.Data.GetFiles();
            var zipFiles = files?.Where(f =>
                _allowedExtensions.Any(ae => f.Name.EndsWith(ae, StringComparison.OrdinalIgnoreCase)));

            if (zipFiles == null) return;

            await ViewModel.LoadArchiveFilesAsync(zipFiles.Select(f => f.Path.LocalPath).ToList());
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        var files = e.Data.GetFiles();
        if (files?.Any(f => _allowedExtensions.Any(ae => f.Name.EndsWith(ae, StringComparison.OrdinalIgnoreCase))) !=
            true) return;

        e.DragEffects = DragDropEffects.Copy;
        DropZone.Background = Avalonia.Media.Brushes.LightBlue;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = GetTopLevel(this)!;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Asset ZIP files",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("ZIP Archives")
                        { Patterns = _allowedExtensions.Select(ae => $"*{ae}").ToArray() },
                    new FilePickerFileType("All Files") { Patterns = ["*"] }
                ]
            });

            if (files.Count < 1) return;

            await ViewModel.LoadArchiveFilesAsync(files.Select(f => f.Path.LocalPath).ToList());
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            var currentLocation = System.AppContext.BaseDirectory;
            SharpSevenZipBase.SetLibraryPath(Path.Combine(currentLocation, Environment.Is64BitProcess ? "x64" : "x86",
                "7z.dll"));

            if (Application.Current?.ApplicationLifetime is null)
                return;

            await ViewModel.LoadAssetLibrariesAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }

    private static async Task ShowErrorMessageBox(Exception ex)
    {
        var box = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
        {
            ContentTitle = "Error",
            ContentMessage = ex.Message,
            Icon = MsBox.Avalonia.Enums.Icon.Error,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ButtonDefinitions =
            [
                new ButtonDefinition { Name = "Ok", IsDefault = true }
            ]
        });

        await box.ShowAsync();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var settingsWindow = new SettingsWindow()
            {
                DataContext = ServiceCollectionExtensions.GetServiceProvider()
                    .GetRequiredService<SettingsWindowViewModel>()
            };

            var result = await settingsWindow.ShowDialog<bool>(this);
            if (result)
            {
                await ViewModel.LoadAssetLibrariesAsync();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }

    private void ArchivesDataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not DataGrid grid) return;

        vm.SelectedArchives.Clear();
        foreach (var item in grid.SelectedItems) vm.SelectedArchives.Add((LoadedArchive)item);
    }

    private async void AssetLibrary_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm || sender is not ComboBox cb) return;

            await vm.LoadInstalledArchivesAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }

    private async void InstalledArchivesTreeView_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm || sender is not TreeView tree) return;

            await vm.UpdateInstalledAssetDetailsAsync(e.AddedItems.Cast<TreeNode>().FirstOrDefault());
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }
}