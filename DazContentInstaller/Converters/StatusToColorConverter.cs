using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DazContentInstaller.Database;

namespace DazContentInstaller.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || !Enum.TryParse(value.ToString(), out ArchiveStatus status))
            return Brushes.Black;
        
        return status switch
        {
            ArchiveStatus.Error => Brushes.Red,
            ArchiveStatus.Installing => Brushes.Green,
            ArchiveStatus.Loading => Brushes.Blue,
            ArchiveStatus.Ready => Brushes.Orange,
            ArchiveStatus.Installed => Brushes.DarkGreen,
            _ => Brushes.Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}