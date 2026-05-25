using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DazContentInstaller.Database;
using DazContentInstaller.Extensions;

namespace DazContentInstaller.Converters;

public class ArchiveStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ArchiveStatus.Ready => new SolidColorBrush(Color.Parse("#22C55E")),
            ArchiveStatus.Pending => new SolidColorBrush(Color.Parse("#64748B")),
            ArchiveStatus.Installed => new SolidColorBrush(Color.Parse("#4CAF50")),
            ArchiveStatus.Installing => new SolidColorBrush(Color.Parse("#4F7CFF")),
            ArchiveStatus.Loading => new SolidColorBrush(Color.Parse("#C48C2A")),
            ArchiveStatus.Duplicate => new SolidColorBrush(Color.Parse("#8B5CF6")),
            ArchiveStatus.Uninstalled => new SolidColorBrush(Color.Parse("#7D8796")),
            ArchiveStatus.Error => new SolidColorBrush(Color.Parse("#D05C5C")),
            _ => new SolidColorBrush(Color.Parse("#A9B5C4"))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class AssetTypeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return ThemeBrushConverter.GetAssetTypeBrush(value);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class CategoryToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return ThemeBrushConverter.GetCategoryBrush(value);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class FileSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ulong bytes => FileSizeFormatter.Format(bytes),
            long bytes and >= 0 => FileSizeFormatter.Format((ulong)bytes),
            int bytes and >= 0 => FileSizeFormatter.Format((ulong)bytes),
            _ => "0 B"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class FilterSegmentBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
            return Brushes.Transparent;

        return (parameter as string)?.ToLowerInvariant() switch
        {
            "all" => new SolidColorBrush(Color.Parse("#4F7CFF")),
            "uninstalled" => new SolidColorBrush(Color.Parse("#7D8796")),
            "failed" => new SolidColorBrush(Color.Parse("#D05C5C")),
            _ => Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class FilterSegmentForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? Brushes.White
            : new SolidColorBrush(Color.Parse("#A9B5C4"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
