using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Numerics;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The "tab is full" notice: a small pill that slides in at the bottom-right of the editor, holds
/// briefly, and slides out on its own. Presentation only — it shows what it is told to show. The
/// decision of <em>whether</em> to show it belongs to <see cref="LimitNotice"/>.
/// </summary>
public sealed partial class LimitNoticeView : UserControl
{
    private const int VisibleMilliseconds = 3000;
    private const int AnimationMilliseconds = 200;

    // Matches the tab-edit flyout's EntranceThemeTransition: an 8px vertical slide paired with the
    // fade, so the notice enters and leaves the same way the app's other popups do.
    private const double EntranceOffset = 8;

    private const float ShadowBlurRadius = 16;
    private const float ShadowOpacity = 0.28f;
    private const float ShadowOffsetY = 4;

    private const string NoticeMessage = "Slate is full. Text limit reached.";

    private readonly DispatcherTimer holdTimer;

    private DropShadow dropShadow;
    private SpriteVisual shadowVisual;

    /// <summary>Builds the notice, hidden.</summary>
    public LimitNoticeView()
    {
        InitializeComponent();

        BuildShadow();
        NoticeHost.SizeChanged += OnNoticeHostSizeChanged;

        holdTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(VisibleMilliseconds),
        };
        holdTimer.Tick += OnHoldTimerTick;
    }

    /// <summary>Whether the notice is currently on screen.</summary>
    public bool IsShowing => NoticeRoot.Visibility == Visibility.Visible;

    /// <summary>
    /// Slides the notice in if it is not already up, and (re)starts the hold before it slides out.
    /// </summary>
    public void Show()
    {
        NoticeText.Text = NoticeMessage;
        AutomationProperties.SetName(NoticeHost, NoticeMessage);

        holdTimer.Stop();

        if (IsShowing == false)
        {
            NoticeRoot.Visibility = Visibility.Visible;
            AnimateIn();
            AnnounceToScreenReader();
        }

        holdTimer.Start();
    }

    /// <summary>
    /// Restarts the hold on a notice that is already up, without re-wording or re-animating it. The
    /// user is still hitting the cap, so the notice must not time out from under them.
    /// </summary>
    public void HoldOpen()
    {
        if (IsShowing == false)
        {
            return;
        }

        holdTimer.Stop();
        holdTimer.Start();
    }

    /// <summary>Drops the notice at once, with no animation. Used when it stops being relevant.</summary>
    public void DismissImmediately()
    {
        holdTimer.Stop();
        SetHiddenState();
        NoticeRoot.Visibility = Visibility.Collapsed;
    }

    private void BuildShadow()
    {
        Visual hostVisual = ElementCompositionPreview.GetElementVisual(ShadowHost);
        Compositor compositor = hostVisual.Compositor;

        dropShadow = compositor.CreateDropShadow();
        dropShadow.Color = Colors.Black;
        dropShadow.Opacity = ShadowOpacity;
        dropShadow.BlurRadius = ShadowBlurRadius;
        dropShadow.Offset = new Vector3(0, ShadowOffsetY, 0);

        shadowVisual = compositor.CreateSpriteVisual();
        shadowVisual.Shadow = dropShadow;

        ElementCompositionPreview.SetElementChildVisual(ShadowHost, shadowVisual);
    }

    private void OnNoticeHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncShadowGeometry();
    }

    // The shadow is masked to the pill's rounded-rectangle so it does not bleed past the corners.
    // A Border exposes no alpha mask of its own, so the mask is built from a shape visual matching
    // the pill's size and corner radius, rendered to a surface, and handed to the shadow.
    private void SyncShadowGeometry()
    {
        float width = (float)NoticeHost.ActualWidth;
        float height = (float)NoticeHost.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Compositor compositor = shadowVisual.Compositor;
        var size = new Vector2(width, height);
        shadowVisual.Size = size;

        CompositionRoundedRectangleGeometry geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = size;
        geometry.CornerRadius = new Vector2((float)NoticeHost.CornerRadius.TopLeft);

        CompositionSpriteShape shape = compositor.CreateSpriteShape(geometry);
        shape.FillBrush = compositor.CreateColorBrush(Colors.Black);

        ShapeVisual maskVisual = compositor.CreateShapeVisual();
        maskVisual.Size = size;
        maskVisual.Shapes.Add(shape);

        CompositionVisualSurface maskSurface = compositor.CreateVisualSurface();
        maskSurface.SourceVisual = maskVisual;
        maskSurface.SourceSize = size;

        dropShadow.Mask = compositor.CreateSurfaceBrush(maskSurface);
    }

    /// <summary>
    /// The notice is a live region, but a live region is only announced when something raises the
    /// event — nothing does so implicitly for a Border whose child text changed.
    /// </summary>
    private void AnnounceToScreenReader()
    {
        AutomationPeer peer = FrameworkElementAutomationPeer.FromElement(NoticeHost);
        if (peer == null)
        {
            peer = FrameworkElementAutomationPeer.CreatePeerForElement(NoticeHost);
        }

        if (peer != null)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void OnHoldTimerTick(object sender, object e)
    {
        holdTimer.Stop();
        AnimateOut();
    }

    // The pill and its shadow host animate identically so the shadow tracks the pill: opacity and an
    // 8px vertical slide, eased like the tab-edit flyout's entrance.
    private void AnimateIn()
    {
        var storyboard = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AddSlideAndFade(storyboard, EntranceOffset, 0, 1.0, ease);
        storyboard.Begin();
    }

    private void AnimateOut()
    {
        var storyboard = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        AddSlideAndFade(storyboard, 0, EntranceOffset, 0.0, ease);
        storyboard.Completed += (s, e) => NoticeRoot.Visibility = Visibility.Collapsed;
        storyboard.Begin();
    }

    private void AddSlideAndFade(Storyboard storyboard, double fromY, double toY, double toOpacity, EasingFunctionBase ease)
    {
        storyboard.Children.Add(Animate(NoticeSlide, "Y", fromY, toY, ease));
        storyboard.Children.Add(Animate(ShadowSlide, "Y", fromY, toY, ease));
        storyboard.Children.Add(Animate(NoticeHost, "Opacity", NoticeHost.Opacity, toOpacity, ease));
        storyboard.Children.Add(Animate(ShadowHost, "Opacity", ShadowHost.Opacity, toOpacity, ease));
    }

    private static DoubleAnimation Animate(DependencyObject target, string property, double from, double to, EasingFunctionBase ease)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(AnimationMilliseconds)),
            EasingFunction = ease,
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private void SetHiddenState()
    {
        NoticeHost.Opacity = 0;
        ShadowHost.Opacity = 0;
        NoticeSlide.Y = EntranceOffset;
        ShadowSlide.Y = EntranceOffset;
    }
}
