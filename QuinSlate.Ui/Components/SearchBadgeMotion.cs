using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The show/hide motion of a tab's search badge (<c>Docs/Specs/24-SEARCH.md § Tab badges</c>): a
/// short scale-and-fade from the badge's centre, played by Composition whenever its
/// <see cref="UIElement.Visibility"/> flips. A count change keeps the badge visible, so it never
/// animates.
/// </summary>
/// <remarks>
/// Layout sees a hidden badge as collapsed straight away, so the tab title reclaims its room in the
/// same pass; only the badge's visual lingers for the hide.
/// </remarks>
internal static class SearchBadgeMotion
{
    private const float HiddenScale = 0.6f;
    private const float ShownScale = 1f;
    private const float HiddenOpacity = 0f;
    private const float ShownOpacity = 1f;
    private const float StartProgress = 0f;
    private const float EndProgress = 1f;
    private const int ShowDurationMilliseconds = 150;
    private const int HideDurationMilliseconds = 120;
    private const string ScaleProperty = "Scale";
    private const string OpacityProperty = "Opacity";
    private const string CenterPointProperty = "CenterPoint";
    private const string CentreExpression = "Vector3(this.Target.Size.X * 0.5, this.Target.Size.Y * 0.5, 0)";

    // Fluent's decelerate curve: fast off the mark, settling into place.
    private static readonly Vector2 EaseOutControlPoint1 = new Vector2(0f, 0f);
    private static readonly Vector2 EaseOutControlPoint2 = new Vector2(0f, 1f);

    /// <summary>
    /// Gives <paramref name="badge"/> its show and hide animations, unless the user has turned
    /// animations off in Windows.
    /// </summary>
    public static void Attach(FrameworkElement badge)
    {
        if (badge == null)
        {
            throw new ArgumentNullException(nameof(badge));
        }

        if (new UISettings().AnimationsEnabled == false)
        {
            return;
        }

        Visual visual = ElementCompositionPreview.GetElementVisual(badge);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction easeOut = compositor.CreateCubicBezierEasingFunction(EaseOutControlPoint1, EaseOutControlPoint2);

        ElementCompositionPreview.SetImplicitShowAnimation(
            badge,
            ScaleAndFade(compositor, easeOut, HiddenScale, ShownScale, HiddenOpacity, ShownOpacity, ShowDurationMilliseconds));
        ElementCompositionPreview.SetImplicitHideAnimation(
            badge,
            ScaleAndFade(compositor, easeOut, ShownScale, HiddenScale, ShownOpacity, HiddenOpacity, HideDurationMilliseconds));

        // XAML keeps the hand-out visual's Size in step with layout, so the centre follows the badge
        // as "9" becomes "10" without a layout handler.
        ExpressionAnimation centre = compositor.CreateExpressionAnimation(CentreExpression);
        centre.Target = CenterPointProperty;
        visual.StartAnimation(CenterPointProperty, centre);
    }

    private static CompositionAnimationGroup ScaleAndFade(
        Compositor compositor,
        CompositionEasingFunction ease,
        float fromScale,
        float toScale,
        float fromOpacity,
        float toOpacity,
        int durationMilliseconds)
    {
        TimeSpan duration = TimeSpan.FromMilliseconds(durationMilliseconds);

        Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Target = ScaleProperty;
        scale.Duration = duration;
        scale.InsertKeyFrame(StartProgress, new Vector3(fromScale, fromScale, ShownScale));
        scale.InsertKeyFrame(EndProgress, new Vector3(toScale, toScale, ShownScale), ease);

        ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Target = OpacityProperty;
        opacity.Duration = duration;
        opacity.InsertKeyFrame(StartProgress, fromOpacity);
        opacity.InsertKeyFrame(EndProgress, toOpacity, ease);

        CompositionAnimationGroup group = compositor.CreateAnimationGroup();
        group.Add(scale);
        group.Add(opacity);
        return group;
    }
}
