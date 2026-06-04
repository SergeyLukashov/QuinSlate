using System;
using System.Collections.Generic;

namespace Jott.AssetGenerator.Catalog;

/// <summary>
/// Produces the full set of asset specifications a WinUI 3 / MSIX application
/// needs: every tile across the standard display scales, the Square44x44 target
/// sizes, the store logo, the wide tile, and the splash screen.
/// </summary>
public static class WinUiAssetCatalog
{
    private const int Square44Base = 44;
    private const int Square71Base = 71;
    private const int Square150Base = 150;
    private const int Square310Base = 310;
    private const int StoreLogoBase = 50;
    private const int LockScreenBase = 24;
    private const int WideWidthBase = 310;
    private const int WideHeightBase = 150;
    private const int SplashWidthBase = 620;
    private const int SplashHeightBase = 300;

    private const int PercentDivisor = 100;
    private const double WideLogoFraction = 2.0 / 3.0;
    private const double SplashLogoFraction = 0.5;

    private static readonly int[] Scales = { 100, 125, 150, 200, 400 };
    private static readonly int[] TargetSizes = { 16, 24, 32, 48, 256 };

    /// <summary>
    /// Builds the complete list of assets to generate.
    /// </summary>
    public static IReadOnlyList<AssetSpecification> Create()
    {
        var specs = new List<AssetSpecification>();

        AddSquareTile(specs, "Square44x44Logo", Square44Base);
        AddSquareTile(specs, "Square71x71Logo", Square71Base);
        AddSquareTile(specs, "Square150x150Logo", Square150Base);
        AddSquareTile(specs, "Square310x310Logo", Square310Base);
        AddSquareTile(specs, "StoreLogo", StoreLogoBase);
        AddSquareTile(specs, "LockScreenLogo", LockScreenBase);

        AddCenteredTile(specs, "Wide310x150Logo", WideWidthBase, WideHeightBase, WideLogoFraction);
        AddCenteredTile(specs, "SplashScreen", SplashWidthBase, SplashHeightBase, SplashLogoFraction);

        AddTargetSizes(specs);

        return specs;
    }

    private static void AddSquareTile(List<AssetSpecification> specs, string baseName, int baseSize)
    {
        foreach (var scale in Scales)
        {
            int size = Scaled(baseSize, scale);
            specs.Add(new AssetSpecification($"{baseName}.scale-{scale}.png", size, size, AssetPlacement.Fill, 0));
        }
    }

    private static void AddCenteredTile(List<AssetSpecification> specs, string baseName, int baseWidth, int baseHeight, double logoFraction)
    {
        foreach (var scale in Scales)
        {
            int width = Scaled(baseWidth, scale);
            int height = Scaled(baseHeight, scale);
            int logoSize = (int)Math.Round(height * logoFraction, MidpointRounding.AwayFromZero);
            specs.Add(new AssetSpecification($"{baseName}.scale-{scale}.png", width, height, AssetPlacement.Centered, logoSize));
        }
    }

    private static void AddTargetSizes(List<AssetSpecification> specs)
    {
        foreach (var size in TargetSizes)
        {
            specs.Add(new AssetSpecification($"Square44x44Logo.targetsize-{size}.png", size, size, AssetPlacement.Fill, 0));
            specs.Add(new AssetSpecification($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size, size, AssetPlacement.Fill, 0));
        }
    }

    private static int Scaled(int baseSize, int scalePercent)
    {
        return (int)Math.Round((double)baseSize * scalePercent / PercentDivisor, MidpointRounding.AwayFromZero);
    }
}
