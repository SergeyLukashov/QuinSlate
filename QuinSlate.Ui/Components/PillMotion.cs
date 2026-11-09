using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The enter/leave motion shared by the floating pills over the editor: an 8px vertical slide paired
/// with a fade, matching the tab-edit flyout's <c>EntranceThemeTransition</c>. The pill and its
/// <see cref="PillDropShadow"/> host move identically so the shadow tracks the pill.
/// </summary>
internal static class PillMotion
{
    /// <summary>How far a pill slides while it fades, in DIPs.</summary>
    public const double SlideDistance = 8;

    private const int DurationMilliseconds = 200;
    private const string TranslateYProperty = "Y";
    private const string OpacityProperty = "Opacity";

    /// <summary>
    /// Builds a storyboard that slides both transforms from <paramref name="fromY"/> to
    /// <paramref name="toY"/> and fades both elements from their current opacity to
    /// <paramref name="toOpacity"/>.
    /// </summary>
    public static Storyboard SlideAndFade(
        TranslateTransform pillSlide,
        TranslateTransform shadowSlide,
        UIElement pill,
        UIElement shadow,
        double fromY,
        double toY,
        double toOpacity,
        EasingMode easingMode)
    {
        var ease = new CubicEase { EasingMode = easingMode };
        var storyboard = new Storyboard();
        storyboard.Children.Add(Animate(pillSlide, TranslateYProperty, fromY, toY, ease));
        storyboard.Children.Add(Animate(shadowSlide, TranslateYProperty, fromY, toY, ease));
        storyboard.Children.Add(Animate(pill, OpacityProperty, pill.Opacity, toOpacity, ease));
        storyboard.Children.Add(Animate(shadow, OpacityProperty, shadow.Opacity, toOpacity, ease));
        return storyboard;
    }

    private static DoubleAnimation Animate(DependencyObject target, string property, double from, double to, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(DurationMilliseconds)),
            EasingFunction = ease,
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
