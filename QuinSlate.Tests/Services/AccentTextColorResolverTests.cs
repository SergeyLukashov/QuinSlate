using QuinSlate.Ui.Services;
using Windows.UI;

namespace QuinSlate.Tests.Services;

public sealed class AccentTextColorResolverTests
{
    // The gradient mesh mid-tones the editor actually draws on: what
    // DitheredGradientBrushFactory.MidColor averages out of App.xaml's four AppGradient* corners
    // today. Copied rather than called because MidColor reads XAML resources and these tests have no
    // Application. The resolver's guarantee holds for any background, so a drift in App.xaml would
    // date these constants without invalidating the assertions.
    private static readonly Color DarkBackground = Color.FromArgb(0xFF, 0x29, 0x28, 0x26);
    private static readonly Color LightBackground = Color.FromArgb(0xFF, 0xF9, 0xF8, 0xF4);

    private static readonly Color White = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color Black = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    private static Color Rgb(byte r, byte g, byte b)
    {
        return Color.FromArgb(0xFF, r, g, b);
    }

    [Fact]
    public void ContrastRatio_BlackOnWhite_IsTwentyOne()
    {
        Assert.Equal(21.0, AccentTextColorResolver.ContrastRatio(Black, White), 3);
    }

    [Fact]
    public void ContrastRatio_IdenticalColors_IsOne()
    {
        Assert.Equal(1.0, AccentTextColorResolver.ContrastRatio(DarkBackground, DarkBackground), 3);
    }

    [Fact]
    public void ContrastRatio_IsSymmetric()
    {
        double forward = AccentTextColorResolver.ContrastRatio(Black, White);
        double backward = AccentTextColorResolver.ContrastRatio(White, Black);
        Assert.Equal(forward, backward, 6);
    }

    [Fact]
    public void DefaultWindowsBlue_AccentLight3_PassesOnDarkUntouched()
    {
        // #99EBFF is AccentLight3 for the stock #0078D4 accent — what WinUI's
        // AccentTextFillColorPrimaryBrush resolves to in dark theme.
        Color light3 = Rgb(0x99, 0xEB, 0xFF);
        Color resolved = AccentTextColorResolver.Resolve(light3, DarkBackground, White);
        Assert.Equal(light3, resolved);
    }

    [Fact]
    public void DefaultWindowsBlue_AccentDark2_PassesOnLightUntouched()
    {
        // #003E92 is AccentDark2 for the stock accent — WinUI's light-theme accent text colour.
        Color dark2 = Rgb(0x00, 0x3E, 0x92);
        Color resolved = AccentTextColorResolver.Resolve(dark2, LightBackground, Black);
        Assert.Equal(dark2, resolved);
    }

    [Fact]
    public void RawAccent_IsTooDimOnDark_AndGetsLifted()
    {
        // The bug this resolver exists for: the raw accent mid-tone against the dark mesh.
        Color rawAccent = Rgb(0x00, 0x78, 0xD4);
        Assert.True(AccentTextColorResolver.ContrastRatio(rawAccent, DarkBackground) < 4.5);

        Color resolved = AccentTextColorResolver.Resolve(rawAccent, DarkBackground, White);
        Assert.NotEqual(rawAccent, resolved);
        Assert.True(AccentTextColorResolver.ContrastRatio(resolved, DarkBackground) >= 4.5);
    }

    [Theory]
    // A near-black navy: the pathological case the OS shade algorithm does not promise to fix.
    [InlineData(0x00, 0x1A, 0x68)]
    // A dark accent that is already close to the dark mesh's own tone.
    [InlineData(0x33, 0x30, 0x2C)]
    // Fully saturated primaries and a mid grey, as accents.
    [InlineData(0xFF, 0x00, 0x00)]
    [InlineData(0x00, 0x00, 0xFF)]
    [InlineData(0x80, 0x80, 0x80)]
    [InlineData(0x00, 0x00, 0x00)]
    public void AnyAccent_ClearsTheFloorOnDark(byte r, byte g, byte b)
    {
        Color resolved = AccentTextColorResolver.Resolve(Rgb(r, g, b), DarkBackground, White);
        Assert.True(
            AccentTextColorResolver.ContrastRatio(resolved, DarkBackground) >= 4.5,
            $"#{r:X2}{g:X2}{b:X2} resolved below the contrast floor on the dark mesh.");
    }

    [Theory]
    [InlineData(0x99, 0xEB, 0xFF)]
    [InlineData(0xFF, 0xFF, 0x00)]
    [InlineData(0x80, 0x80, 0x80)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    public void AnyAccent_ClearsTheFloorOnLight(byte r, byte g, byte b)
    {
        Color resolved = AccentTextColorResolver.Resolve(Rgb(r, g, b), LightBackground, Black);
        Assert.True(
            AccentTextColorResolver.ContrastRatio(resolved, LightBackground) >= 4.5,
            $"#{r:X2}{g:X2}{b:X2} resolved below the contrast floor on the light mesh.");
    }

    [Fact]
    public void LiftedColor_StaysAsCloseToTheAccentAsTheFloorAllows()
    {
        // The blend is the *smallest* one that clears the bar: a hair under the floor still
        // leaves it near the original, not snapped to the reinforcement.
        Color dimBlue = Rgb(0x00, 0x60, 0xB0);
        Color resolved = AccentTextColorResolver.Resolve(dimBlue, DarkBackground, White);

        Assert.True(AccentTextColorResolver.ContrastRatio(resolved, DarkBackground) >= 4.5);
        // Landed close to the floor rather than overshooting to white (21:1 on this background).
        Assert.True(AccentTextColorResolver.ContrastRatio(resolved, DarkBackground) < 5.0);
        Assert.NotEqual(White, resolved);
    }

    [Fact]
    public void ResolvedColor_IsAlwaysOpaque()
    {
        Color resolved = AccentTextColorResolver.Resolve(Rgb(0x00, 0x00, 0x00), DarkBackground, White);
        Assert.Equal(0xFF, resolved.A);
    }

    [Fact]
    public void ImpossibleBackground_ReturnsTheReinforcement()
    {
        // White text on a white surface: no blend toward white can ever clear the floor, so the
        // resolver gives back the reinforcement rather than looping or returning the dim original.
        Color resolved = AccentTextColorResolver.Resolve(Rgb(0xEE, 0xEE, 0xEE), White, White);
        Assert.Equal(White, resolved);
    }
}
