using System;
using Avalonia.Controls;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
            await viewModel.InitializeAsync();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
            viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, bool result)
    {
        Close(result);
    }
}
