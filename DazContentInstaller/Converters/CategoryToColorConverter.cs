using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DazContentInstaller.Converters;

public class CategoryToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value as string;
        return category switch
        {
            "characters" => Brushes.Bisque,
            "anatomy" => Brushes.Red,
            "clothing" or "wardrobe" => Brushes.Blue,
            "hair" => Brushes.Green,
            "props" or "vehicles" => Brushes.Orange,
            "environments" or "scenes" => Brushes.OrangeRed,
            "poses" or "animations" or "morphs" => Brushes.Aqua,
            "materials" or "shaders" => Brushes.Violet,
            "lights" => Brushes.Pink,
            "cameras" => Brushes.DarkSlateGray,
            "scripts" => Brushes.Black,
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class CategoryToFontColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value as string;
        return category switch
        {
            "characters" or "environments" or "scenes" or "props" or "vehicles" or "poses" or "animations" or "morphs"
                or "materials" or "shaders" or "lights" => Brushes.Black,
            _ => Brushes.White
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}