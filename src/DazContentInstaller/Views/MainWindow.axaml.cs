using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
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