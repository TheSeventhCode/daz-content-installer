using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DazContentInstaller.Database;

namespace DazContentInstaller.Converters;

internal static class ThemeBrushConverter
{
    public static IBrush GetBrush(string resourceKey, string fallbackHex)
    {
        if (Application.Current is IResourceHost host
            && host.TryFindResource(resourceKey, out var resource)
            && resource is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    public static IBrush GetAssetTypeBrush(object? value)
    {
        var assetType = value switch
        {
            AssetType type => type,
            string text when Enum.TryParse<AssetType>(text, out var parsed) => parsed,
            _ => AssetType.Unknown
        };

        var (resourceKey, fallbackHex) = assetType switch
        {
            AssetType.Character => ("AssetTypeCharacterBrush", "#6366F1"),
            AssetType.Anatomy => ("AssetTypeAnatomyBrush", "#EC4899"),
            AssetType.Clothing => ("AssetTypeClothingBrush", "#8B5CF6"),
            AssetType.Hair => ("AssetTypeHairBrush", "#D97706"),
            AssetType.Props => ("AssetTypePropsBrush", "#14B8A6"),
            AssetType.Environment => ("AssetTypeEnvironmentBrush", "#22C55E"),
            AssetType.Poses => ("AssetTypePosesBrush", "#3B82F6"),
            AssetType.Materials => ("AssetTypeMaterialsBrush", "#A855F7"),
            AssetType.Lights => ("AssetTypeLightsBrush", "#CA8A04"),
            AssetType.Cameras => ("AssetTypeCamerasBrush", "#06B6D4"),
            AssetType.Scripts => ("AssetTypeScriptsBrush", "#64748B"),
            AssetType.Textures => ("AssetTypeTexturesBrush", "#E879F9"),
            AssetType.Morphs => ("AssetTypeMorphsBrush", "#FB7185"),
            _ => ("AssetTypeUnknownBrush", "#7D8796")
        };

        return GetBrush(resourceKey, fallbackHex);
    }
}
