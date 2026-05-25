using System;
using Avalonia.Controls;
using DazContentInstaller.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using MsBox.Avalonia.Enums;

namespace DazContentInstaller.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsBusy)
            return;

        e.Cancel = true;

        var messageBox = MessageBoxManager.GetMessageBoxStandard(
            "Operation in progress",
            "A scan, install, or uninstall is still running. Please wait for it to finish before closing the window.",
            ButtonEnum.Ok,
            MsBoxIcon.Warning);

        await messageBox.ShowWindowDialogAsync(this);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            await viewModel.InitializeAsync();
    }

    private async void OpenSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var services = ServiceCollectionExtensions.GetServiceProvider();
        var settingsWindow = services.GetRequiredService<SettingsWindow>();
        settingsWindow.DataContext = services.GetRequiredService<SettingsWindowViewModel>();

        var result = await settingsWindow.ShowDialog<bool>(this);
        if (result && DataContext is MainWindowViewModel viewModel)
            await viewModel.ReloadAfterSettingsAsync();
    }
}