using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuinSlate.Ui.Layout;
using System;
using Windows.Foundation;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Shared pointer behaviour for an emoji canvas surface. Tracks the pointer
/// over the cell grid with pure hit-test math (no per-cell elements, handlers,
/// or automation peers), moves the single hover/pressed highlight pair under
/// it, and raises <see cref="CellTapped"/> with the hit cell index on tap.
/// Tap (not press/release) is used for selection so touch panning inside the
/// owning ScrollViewer never mis-picks.
/// </summary>
internal sealed class EmojiCanvasInteraction
{
    private readonly Canvas canvas;
    private readonly Border hoverHighlight;
    private readonly Border pressedHighlight;

    private EmojiSheetLayout currentLayout;
    private int lastHitCellIndex = EmojiSheetLayoutCalculator.NoHit;
    private bool isPressed;

    /// <summary>Raised when the user taps a populated cell. The argument is the hit cell index.</summary>
    internal event EventHandler<int> CellTapped;

    /// <summary>
    /// Wires pointer handling onto <paramref name="canvas"/>. The two highlight
    /// borders must be children of the canvas, below the glyphs in z-order.
    /// </summary>
    internal EmojiCanvasInteraction(Canvas canvas, Border hoverHighlight, Border pressedHighlight)
    {
        if (canvas == null)
        {
            throw new ArgumentNullException(nameof(canvas));
        }

        if (hoverHighlight == null)
        {
            throw new ArgumentNullException(nameof(hoverHighlight));
        }

        if (pressedHighlight == null)
        {
            throw new ArgumentNullException(nameof(pressedHighlight));
        }

        this.canvas = canvas;
        this.hoverHighlight = hoverHighlight;
        this.pressedHighlight = pressedHighlight;

        canvas.PointerMoved += OnPointerMoved;
        canvas.PointerExited += OnPointerExited;
        canvas.PointerPressed += OnPointerPressed;
        canvas.PointerReleased += OnPointerReleased;
        canvas.PointerCanceled += OnPointerLost;
        canvas.PointerCaptureLost += OnPointerLost;
        canvas.Tapped += OnTapped;
    }

    /// <summary>
    /// Sets the layout used for hit-testing and resets any transient pointer
    /// state. Call whenever the owning surface repositions its glyphs.
    /// </summary>
    internal void SetLayout(EmojiSheetLayout layout)
    {
        currentLayout = layout;
        lastHitCellIndex = EmojiSheetLayoutCalculator.NoHit;
        isPressed = false;
        UpdateHighlight();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdateHitFromPoint(e.GetCurrentPoint(canvas).Position);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        isPressed = true;
        UpdateHitFromPoint(e.GetCurrentPoint(canvas).Position);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        isPressed = false;
        UpdateHitFromPoint(e.GetCurrentPoint(canvas).Position);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        isPressed = false;
        lastHitCellIndex = EmojiSheetLayoutCalculator.NoHit;
        UpdateHighlight();
    }

    private void OnPointerLost(object sender, PointerRoutedEventArgs e)
    {
        isPressed = false;
        lastHitCellIndex = EmojiSheetLayoutCalculator.NoHit;
        UpdateHighlight();
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        Point position = e.GetPosition(canvas);
        int cellIndex = EmojiSheetLayoutCalculator.HitTest(currentLayout, position.X, position.Y);
        if (cellIndex != EmojiSheetLayoutCalculator.NoHit)
        {
            CellTapped?.Invoke(this, cellIndex);
        }
    }

    private void UpdateHitFromPoint(Point position)
    {
        lastHitCellIndex = EmojiSheetLayoutCalculator.HitTest(currentLayout, position.X, position.Y);
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        bool isHit = currentLayout != null
            && lastHitCellIndex != EmojiSheetLayoutCalculator.NoHit
            && lastHitCellIndex < currentLayout.Cells.Count;

        if (!isHit)
        {
            hoverHighlight.Visibility = Visibility.Collapsed;
            pressedHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        EmojiCellPosition cell = currentLayout.Cells[lastHitCellIndex];
        Border active = isPressed ? pressedHighlight : hoverHighlight;
        Border inactive = isPressed ? hoverHighlight : pressedHighlight;

        Canvas.SetLeft(active, cell.X + EmojiSheetLayoutCalculator.ItemMargin);
        Canvas.SetTop(active, cell.Y + EmojiSheetLayoutCalculator.ItemMargin);
        active.Visibility = Visibility.Visible;
        inactive.Visibility = Visibility.Collapsed;
    }
}
