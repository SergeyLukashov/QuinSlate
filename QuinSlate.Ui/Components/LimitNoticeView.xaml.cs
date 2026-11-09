using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The "tab is full" notice: a small pill that slides in at the bottom-right of the editor, holds
/// briefly, and slides out on its own. Presentation only — it shows what it is told to show. The
/// decision of <em>whether</em> to show it belongs to <see cref="LimitNotice"/>.
/// </summary>
public sealed partial class LimitNoticeView : UserControl
{
    private const int VisibleMilliseconds = 3000;

    // The notice rises into place from below, since it sits at the bottom of the editor.
    private const double EntranceOffset = PillMotion.SlideDistance;

    private const string NoticeMessage = "Slate is full. Text limit reached.";

    private readonly DispatcherTimer holdTimer;

    // Held so the shadow's SizeChanged subscription lives as long as the notice.
    private readonly PillDropShadow shadow;

    /// <summary>Builds the notice, hidden.</summary>
    public LimitNoticeView()
    {
        InitializeComponent();

        shadow = new PillDropShadow(ShadowHost, NoticeHost);

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
            LiveRegionAnnouncer.Announce(NoticeHost);
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

    private void OnHoldTimerTick(object sender, object e)
    {
        holdTimer.Stop();
        AnimateOut();
    }

    private void AnimateIn()
    {
        PillMotion.SlideAndFade(NoticeSlide, ShadowSlide, NoticeHost, ShadowHost, EntranceOffset, 0, 1.0, EasingMode.EaseOut).Begin();
    }

    private void AnimateOut()
    {
        Storyboard storyboard = PillMotion.SlideAndFade(NoticeSlide, ShadowSlide, NoticeHost, ShadowHost, 0, EntranceOffset, 0.0, EasingMode.EaseIn);
        storyboard.Completed += (s, e) => NoticeRoot.Visibility = Visibility.Collapsed;
        storyboard.Begin();
    }

    private void SetHiddenState()
    {
        NoticeHost.Opacity = 0;
        ShadowHost.Opacity = 0;
        NoticeSlide.Y = EntranceOffset;
        ShadowSlide.Y = EntranceOffset;
    }
}
