using Microsoft.UI.Xaml;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

public sealed class ThemeServiceTests
{
    [Fact]
    public void ToElementTheme_System_MapsToDefault()
    {
        Assert.Equal(ElementTheme.Default, ThemeService.ToElementTheme(AppTheme.System));
    }

    [Fact]
    public void ToElementTheme_Light_MapsToLight()
    {
        Assert.Equal(ElementTheme.Light, ThemeService.ToElementTheme(AppTheme.Light));
    }

    [Fact]
    public void ToElementTheme_Dark_MapsToDark()
    {
        Assert.Equal(ElementTheme.Dark, ThemeService.ToElementTheme(AppTheme.Dark));
    }
}
