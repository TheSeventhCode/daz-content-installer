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
            "poses" or "animations" => Brushes.Aqua,
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