using Jott.Ui.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace Jott.Ui.Components;

/// <summary>
/// Provides smooth scrolling behavior for a RichEditBox by intercepting pointer wheel events
/// and animating the viewport change, while protecting the scroll offset from focus-induced jumps.
/// Supports touch panning and scrollbars seamlessly.
/// </summary>
internal sealed class SmoothScrollController
{
    private const double ScrollLineHeight = 24.0;
    private const int LinesPerNotch = 3;
    private const int FocusProtectionWindowMs = 150;
    private const double ScrollbarWidthSafetyMargin = 25.0;

    private RichEditBox editorRef;
    private ScrollViewer scrollViewer;
    private double targetVerticalOffset = -1;
    private DateTime lastWheelTime = DateTime.MinValue;

    private bool isFocusing;
    private double restoreOffset = -1;
    private DateTime focusStartTime = DateTime.MinValue;

    private PointerEventHandler pointerWheelChangedHandler;
    private PointerEventHandler pointerMovedHandler;
    private PointerEventHandler pointerPressedHandler;
    private PointerEventHandler pointerExitedHandler;

    /// <summary>
    /// Registers smooth scrolling behavior on the given editor.
    /// </summary>
    /// <param name="editor">The RichEditBox editor to configure.</param>
    public void Register(RichEditBox editor)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        editor.Loaded += OnEditorLoaded;
        editor.Unloaded += OnEditorUnloaded;
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        var editor = sender as RichEditBox;
        if (editor == null)
        {
            return;
        }

        Cleanup();

        editorRef = editor;

        scrollViewer = VisualTreeHelpers.FindVisualChild<ScrollViewer>(editor);
        if (scrollViewer == null)
        {
            return;
        }

        // Initialize ScrollMode and hook events on the ScrollViewer using AddHandler with handledEventsToo = true
        scrollViewer.VerticalScrollMode = ScrollMode.Enabled;

        pointerWheelChangedHandler = new PointerEventHandler(OnPointerWheelChanged);
        pointerMovedHandler = new PointerEventHandler(OnPointerMoved);
        pointerPressedHandler = new PointerEventHandler(OnPointerPressed);
        pointerExitedHandler = new PointerEventHandler(OnPointerExited);

        scrollViewer.AddHandler(UIElement.PointerWheelChangedEvent, pointerWheelChangedHandler, true);
        scrollViewer.AddHandler(UIElement.PointerMovedEvent, pointerMovedHandler, true);
        scrollViewer.AddHandler(UIElement.PointerPressedEvent, pointerPressedHandler, true);
        scrollViewer.AddHandler(UIElement.PointerExitedEvent, pointerExitedHandler, true);

        editorRef.GettingFocus += OnGettingFocus;
        editorRef.GotFocus += OnGotFocus;
        scrollViewer.ViewChanged += OnViewChanged;
    }

    private void OnEditorUnloaded(object sender, RoutedEventArgs e)
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (editorRef != null)
        {
            editorRef.GettingFocus -= OnGettingFocus;
            editorRef.GotFocus -= OnGotFocus;
        }

        if (scrollViewer != null)
        {
            if (pointerWheelChangedHandler != null)
            {
                scrollViewer.RemoveHandler(UIElement.PointerWheelChangedEvent, pointerWheelChangedHandler);
            }
            if (pointerMovedHandler != null)
            {
                scrollViewer.RemoveHandler(UIElement.PointerMovedEvent, pointerMovedHandler);
            }
            if (pointerPressedHandler != null)
            {
                scrollViewer.RemoveHandler(UIElement.PointerPressedEvent, pointerPressedHandler);
            }
            if (pointerExitedHandler != null)
            {
                scrollViewer.RemoveHandler(UIElement.PointerExitedEvent, pointerExitedHandler);
            }

            scrollViewer.ViewChanged -= OnViewChanged;
            scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
            SetCursor(scrollViewer, Microsoft.UI.Input.InputSystemCursorShape.Arrow, true);
        }

        editorRef = null;
        scrollViewer = null;
        pointerWheelChangedHandler = null;
        pointerMovedHandler = null;
        pointerPressedHandler = null;
        pointerExitedHandler = null;
        targetVerticalOffset = -1;
        restoreOffset = -1;
        isFocusing = false;
        lastWheelTime = DateTime.MinValue;
        focusStartTime = DateTime.MinValue;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdateScrollMode(e);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        UpdateScrollMode(e);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (scrollViewer != null)
        {
            scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
            SetCursor(scrollViewer, Microsoft.UI.Input.InputSystemCursorShape.Arrow, true);
        }
    }

    private void UpdateScrollMode(PointerRoutedEventArgs e)
    {
        if (scrollViewer == null)
        {
            return;
        }

        var pointerPoint = e.GetCurrentPoint(scrollViewer);
        var deviceType = e.Pointer.PointerDeviceType;

        if (deviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            // Touch screen: always enable native panning and default cursor
            scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
            SetCursor(scrollViewer, Microsoft.UI.Input.InputSystemCursorShape.Arrow, true);
        }
        else
        {
            // Mouse/Pen: check if we are over the scrollbar area (right edge)
            double x = pointerPoint.Position.X;
            bool isOverScrollbar = x >= scrollViewer.ActualWidth - ScrollbarWidthSafetyMargin;

            if (isOverScrollbar)
            {
                scrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                SetCursor(scrollViewer, Microsoft.UI.Input.InputSystemCursorShape.Arrow, false);
            }
            else
            {
                scrollViewer.VerticalScrollMode = ScrollMode.Disabled;
                SetCursor(scrollViewer, Microsoft.UI.Input.InputSystemCursorShape.Arrow, true);
            }
        }
    }

    private void SetCursor(UIElement element, Microsoft.UI.Input.InputSystemCursorShape shape, bool resetToDefault)
    {
        if (element == null)
        {
            return;
        }

        try
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop != null)
            {
                if (resetToDefault)
                {
                    prop.SetValue(element, null);
                }
                else
                {
                    var cursor = Microsoft.UI.Input.InputSystemCursor.Create(shape);
                    prop.SetValue(element, cursor);
                }
            }
        }
        catch
        {
            // Ignore reflection failures
        }
    }

    private void OnGettingFocus(UIElement sender, GettingFocusEventArgs e)
    {
        if (scrollViewer != null)
        {
            isFocusing = true;
            restoreOffset = scrollViewer.VerticalOffset;
            focusStartTime = DateTime.UtcNow;
        }
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (scrollViewer != null && isFocusing && restoreOffset >= 0)
        {
            scrollViewer.ChangeView(null, restoreOffset, null, true);

            editorRef.DispatcherQueue.TryEnqueue(() =>
            {
                if (scrollViewer != null && isFocusing && restoreOffset >= 0)
                {
                    scrollViewer.ChangeView(null, restoreOffset, null, true);
                }
            });
        }
    }

    private void OnViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (isFocusing && scrollViewer != null && restoreOffset >= 0)
        {
            if ((DateTime.UtcNow - focusStartTime).TotalMilliseconds > FocusProtectionWindowMs)
            {
                isFocusing = false;
                restoreOffset = -1;
                return;
            }

            if (scrollViewer.VerticalOffset != restoreOffset)
            {
                scrollViewer.ChangeView(null, restoreOffset, null, true);
            }
        }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (scrollViewer == null || scrollViewer.VerticalScrollMode == ScrollMode.Enabled)
        {
            return;
        }

        var properties = e.GetCurrentPoint(scrollViewer).Properties;
        if (properties.IsHorizontalMouseWheel)
        {
            return;
        }

        int delta = properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        // If this is a new scroll sequence (time-based threshold of 250ms),
        // re-initialize targetVerticalOffset to the current actual position.
        if (targetVerticalOffset < 0 || (now - lastWheelTime).TotalMilliseconds > 250)
        {
            targetVerticalOffset = scrollViewer.VerticalOffset;
        }
        lastWheelTime = now;

        // Reset target offset if switching scroll directions to ensure instant response
        if (delta > 0 && targetVerticalOffset > scrollViewer.VerticalOffset)
        {
            // Scrolling UP, but target was overshot downwards
            targetVerticalOffset = scrollViewer.VerticalOffset;
        }
        else if (delta < 0 && targetVerticalOffset < scrollViewer.VerticalOffset)
        {
            // Scrolling DOWN, but target was overshot upwards
            targetVerticalOffset = scrollViewer.VerticalOffset;
        }

        double deltaY = (delta / 120.0) * (LinesPerNotch * ScrollLineHeight);
        targetVerticalOffset -= deltaY;

        // Clamp to physical bounds with a generous layout margin (500px) 
        // to support lazy text measurement during downwards scrolling.
        double maxOffset = scrollViewer.ScrollableHeight + 500.0;
        targetVerticalOffset = Math.Clamp(targetVerticalOffset, -500.0, maxOffset);

        scrollViewer.ChangeView(null, targetVerticalOffset, null, false);

        e.Handled = true;
    }
}
