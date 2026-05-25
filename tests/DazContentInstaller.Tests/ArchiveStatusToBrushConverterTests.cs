using Avalonia.Media;
using DazContentInstaller.Converters;
using DazContentInstaller.Database;
using Shouldly;

namespace DazContentInstaller.Tests;

public class ArchiveStatusToBrushConverterTests
{
    private readonly ArchiveStatusToBrushConverter _converter = new();

    [Fact]
    public void Convert_Pending_ReturnsNeutralBrush()
    {
        var brush = _converter.Convert(ArchiveStatus.Pending, typeof(IBrush), null, null!) as SolidColorBrush;

        brush.ShouldNotBeNull();
        brush.Color.ShouldBe(Color.Parse("#64748B"));
    }

    [Fact]
    public void Convert_Ready_ReturnsGreenBrush()
    {
        var brush = _converter.Convert(ArchiveStatus.Ready, typeof(IBrush), null, null!) as SolidColorBrush;

        brush.ShouldNotBeNull();
        brush.Color.ShouldBe(Color.Parse("#22C55E"));
    }
}
