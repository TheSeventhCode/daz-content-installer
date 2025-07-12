using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DazContentInstaller.Services;
using DazContentInstaller.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;

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

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Background = Avalonia.Media.Brushes.Transparent;

        if (!e.Data.Contains(DataFormats.Files)) return;

        var files = e.Data.GetFiles();
        var zipFiles = files?.Where(f =>
            _allowedExtensions.Any(ae => f.Name.EndsWith(ae, StringComparison.OrdinalIgnoreCase)));

        if (zipFiles == null) return;

        foreach (var file in zipFiles)
            ViewModel.LoadArchiveFile(file.Path.LocalPath);
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
                    new FilePickerFileType("ZIP Archives") { Patterns = _allowedExtensions.Select(ae => $"*{ae}").ToArray() },
                    new FilePickerFileType("All Files") { Patterns = ["*"] }
                ]
            });

            if (files.Count < 1) return;

            foreach (var file in files)
                ViewModel.LoadArchiveFile(file.Path.ToString());
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is null)
            return;
        try
        {
            await ViewModel.LoadAssetLibrariesAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorMessageBox(ex);
        }
    }

    private async Task ShowErrorMessageBox(Exception ex)
    {
        var box = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
        {
            ContentTitle = "Error",
            ContentMessage = ex.Message,
            Icon = MsBox.Avalonia.Enums.Icon.Error,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        });

        await box.ShowAsync();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow()
        {
            DataContext = ServiceCollectionExtensions.GetServiceProvider().GetRequiredService<SettingsWindowViewModel>()
        };
        var result = await settingsWindow.ShowDialog<bool>(this);
        if (result)
        {
            await ViewModel.LoadAssetLibrariesAsync();
        }
    }
}