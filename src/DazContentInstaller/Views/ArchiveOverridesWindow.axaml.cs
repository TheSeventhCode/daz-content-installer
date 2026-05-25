using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using DazContentInstaller.ViewModels;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using MsBox.Avalonia.Enums;

namespace DazContentInstaller.Views;

public partial class ArchiveOverridesWindow : Window
{
    public ArchiveOverridesWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not ArchiveOverridesWindowViewModel viewModel)
            return;

        viewModel.CloseRequested += OnCloseRequested;
        viewModel.ConfirmDeleteRequested += ConfirmDeleteAsync;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is not ArchiveOverridesWindowViewModel viewModel)
            return;

        viewModel.CloseRequested -= OnCloseRequested;
        viewModel.ConfirmDeleteRequested -= ConfirmDeleteAsync;
    }

    private void OnCloseRequested(object? sender, bool hasChanges)
    {
        Close(hasChanges);
    }

    private async Task<bool> ConfirmDeleteAsync(string message)
    {
        var messageBox = MessageBoxManager.GetMessageBoxStandard(
            "Delete override?",
            message,
            ButtonEnum.YesNo,
            MsBoxIcon.Warning);

        var result = await messageBox.ShowWindowDialogAsync(this);
        return result == ButtonResult.Yes;
    }
}
