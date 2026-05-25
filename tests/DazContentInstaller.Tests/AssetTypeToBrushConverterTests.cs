using Avalonia.Media;
using DazContentInstaller.Converters;
using DazContentInstaller.Database;
using Shouldly;

namespace DazContentInstaller.Tests;

public class AssetTypeToBrushConverterTests
{
    private readonly AssetTypeToBrushConverter _converter = new();

    [Theory]
    [InlineData(AssetType.Clothing, "#8B5CF6")]
    [InlineData(AssetType.Textures, "#E879F9")]
    [InlineData(AssetType.Lights, "#CA8A04")]
    public void Convert_returns_distinct_color_for_each_asset_type(AssetType assetType, string expectedHex)
    {
        var brush = _converter.Convert(assetType, typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeOfType<SolidColorBrush>();

        brush.Color.ShouldBe(Color.Parse(expectedHex));
    }

    [Fact]
    public void Convert_parses_asset_type_from_string()
    {
        var brush = _converter.Convert("Morphs", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeOfType<SolidColorBrush>();

        brush.Color.ShouldBe(Color.Parse("#FB7185"));
    }
}
