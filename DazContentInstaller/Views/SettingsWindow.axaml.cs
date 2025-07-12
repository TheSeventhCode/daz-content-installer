using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DazContentInstaller.ViewModels;

namespace DazContentInstaller.Views;

public partial class SettingsWindow : Window
{
    private SettingsWindowViewModel ViewModel =>
        DataContext as SettingsWindowViewModel ?? throw new InvalidOperationException();
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnBrowsePathClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void OnRemoveLibraryClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void OnAddLibraryClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }
    
    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void OnBrowseTempDirectoryClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }
}