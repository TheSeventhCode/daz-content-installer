using DazContentInstaller.Services;
using Shouldly;

namespace DazContentInstaller.Tests;

public class DazProductMetadataReaderTests
{
    private const string ValidSupplementDsx = """
        <ProductSupplement VERSION="0.1">
          <CPA VERSION="1.0.0.30"/>
          <ProductName VALUE="All Business Presentation Poses"/>
          <InstallTypes VALUE="Content"/>
        </ProductSupplement>
        """;

    [Fact]
    public void IsSupplementDsx_MatchesFileNameCaseInsensitively()
    {
        DazProductMetadataReader.IsSupplementDsx("Supplement.dsx").ShouldBeTrue();
        DazProductMetadataReader.IsSupplementDsx("folder/Supplement.DSX").ShouldBeTrue();
        DazProductMetadataReader.IsSupplementDsx("folder/other.dsx").ShouldBeFalse();
    }

    [Fact]
    public void TryReadProductName_ReadsProductNameAttribute()
    {
        DazProductMetadataReader.TryReadProductName(ValidSupplementDsx)
            .ShouldBe("All Business Presentation Poses");
    }

    [Theory]
    [InlineData("<ProductSupplement><ProductName/></ProductSupplement>")]
    [InlineData("<ProductSupplement><Other VALUE=\"Name\"/></ProductSupplement>")]
    [InlineData("not xml")]
    public void TryReadProductName_ReturnsNullForMissingOrInvalidMetadata(string content)
    {
        DazProductMetadataReader.TryReadProductName(content).ShouldBeNull();
    }

    [Fact]
    public void TryReadProductName_TrimsWhitespace()
    {
        const string content = """
            <ProductSupplement>
              <ProductName VALUE="  Trimmed Name  "/>
            </ProductSupplement>
            """;

        DazProductMetadataReader.TryReadProductName(content).ShouldBe("Trimmed Name");
    }
}
