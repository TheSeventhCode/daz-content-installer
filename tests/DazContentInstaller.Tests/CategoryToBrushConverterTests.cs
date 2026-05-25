using Avalonia.Media;
using DazContentInstaller.Converters;
using Shouldly;

namespace DazContentInstaller.Tests;

public class CategoryToBrushConverterTests
{
    private readonly CategoryToBrushConverter _converter = new();

    [Theory]
    [InlineData("hair", "#D97706")]
    [InlineData("characters", "#6366F1")]
    [InlineData("wardrobe", "#8B5CF6")]
    [InlineData("animations", "#F97316")]
    [InlineData("aniBlocks", "#F97316")]
    public void Convert_returns_asset_type_color_for_known_category(string category, string expectedHex)
    {
        var brush = _converter
            .Convert(category, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeOfType<SolidColorBrush>();

        brush.Color.ShouldBe(Color.Parse(expectedHex));
    }

    [Fact]
    public void Convert_returns_unknown_color_for_unmapped_category()
    {
        var brush = _converter
            .Convert("All categories", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeOfType<SolidColorBrush>();

        brush.Color.ShouldBe(Color.Parse("#7D8796"));
    }
}
