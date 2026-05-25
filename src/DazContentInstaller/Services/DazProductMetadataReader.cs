using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DazContentInstaller.Services;

public static class DazProductMetadataReader
{
    public static bool IsSupplementDsx(string normalizedEntryPath)
    {
        return string.Equals(Path.GetFileName(normalizedEntryPath), "Supplement.dsx", StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryReadProductName(string? dsxContent)
    {
        if (string.IsNullOrWhiteSpace(dsxContent))
            return null;

        try
        {
            var document = XDocument.Parse(dsxContent);
            var productNameElement = document.Root?
                .Elements()
                .FirstOrDefault(x => string.Equals(x.Name.LocalName, "ProductName", StringComparison.OrdinalIgnoreCase));

            var value = productNameElement?.Attribute("VALUE")?.Value
                        ?? productNameElement?.Attribute("value")?.Value;

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }
}
