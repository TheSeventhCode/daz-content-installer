using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DazContentInstaller.Models;
using DazContentInstaller.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;

namespace DazContentInstaller.Views;

public partial class SettingsWindow : Window
{
    private SettingsWindowViewModel ViewModel =>
        DataContext as SettingsWindowViewModel ?? throw new InvalidOperationException();

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is null)
            return;

        await ViewModel.LoadSettingsAsync();
    }

    private async void AddLibraryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = GetTopLevel(this)!.StorageProvider;
        var suggestedStartPath =
            await storageProvider.TryGetFolderFromPathAsync(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        var directory = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Library Base Directory",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartPath
        });

        if (directory.Count < 1) return;

        ViewModel.AddLibrary(directory[0]);
    }

    private async void OkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveAsync();
            Close(true);
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
}