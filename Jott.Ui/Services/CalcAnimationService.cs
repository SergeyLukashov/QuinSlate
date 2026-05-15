using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.UI;

namespace Jott.Ui.Services;

/// <summary>
/// Handles the inline calculator replace triggered by typing <c>=</c> at the
/// end of an arithmetic expression, and the subsequent fade animation that
/// highlights the inserted result.
/// </summary>
/// <remarks>
/// Call <see cref="TrackKeyDown"/> from the editor's <c>KeyDown</c> handler
/// and <see cref="HandleTextChanged"/> from the editor's <c>TextChanged</c>
/// handler. Both calls are no-ops when no calculation is in progress.
/// </remarks>
public sealed class CalcAnimationService
{
    private const int AnimationDurationMs = 1600;
    private const int AnimationTickMs = 16;
    private const int VKeyOemPlus = 187;

    private DispatcherTimer animationTimer;
    private RichEditBox animationEditor;
    private int animationResultStart;
    private int animationResultEnd;
    private Color animationAccentColor;
    private Color animationNormalColor;
    private DateTime animationStartTime;
    private int animationExpectedTextLength;

    private bool isCalcReplacing = false;
    private bool wasEqualsTyped = false;

    /// <summary>
    /// Gets whether an animation colour pass is currently being applied.
    /// Callers should skip animation-cancel checks while this is <c>true</c>
    /// to avoid re-entrant cancellation.
    /// </summary>
    public bool IsApplyingColor { get; private set; }

    /// <summary>
    /// Records whether the most recent key press was an unshifted <c>=</c>.
    /// Must be called from the editor's <c>KeyDown</c> handler before the
    /// character is inserted.
    /// </summary>
    /// <param name="keyValue">The integer value of the pressed virtual key.</param>
    /// <param name="isShiftDown">Whether the Shift modifier was held.</param>
    public void TrackKeyDown(int keyValue, bool isShiftDown)
    {
        if (keyValue == VKeyOemPlus && !isShiftDown)
        {
            wasEqualsTyped = true;
        }
    }

    /// <summary>
    /// Evaluates and replaces an inline arithmetic expression when an unshifted
    /// <c>=</c> was the last tracked key, then starts the highlight animation.
    /// Also cancels any running animation if the document text has changed
    /// unexpectedly (e.g. the user typed while the animation was playing).
    /// </summary>
    /// <param name="editor">The <see cref="RichEditBox"/> that raised <c>TextChanged</c>.</param>
    public void HandleTextChanged(RichEditBox editor)
    {
        if (!isCalcReplacing && !IsApplyingColor && animationTimer != null && animationEditor == editor)
        {
            editor.Document.GetText(TextGetOptions.UseCrlf, out string currentText);
            if (currentText.Length != animationExpectedTextLength)
            {
                CancelAnimation();
            }
        }

        if (!isCalcReplacing && wasEqualsTyped)
        {
            wasEqualsTyped = false;
            TryCalcReplace(editor);
        }
        else
        {
            wasEqualsTyped = false;
        }
    }

    /// <summary>
    /// Cancels any animation currently running on any editor.
    /// Safe to call when no animation is active.
    /// </summary>
    public void CancelAnimation()
    {
        if (animationTimer == null)
        {
            return;
        }

        animationTimer.Stop();
        animationTimer.Tick -= OnAnimationTick;
        animationTimer = null;

        ClearAnimationColor();
        animationEditor = null;
    }

    private void TryCalcReplace(RichEditBox editor)
    {
        var sel = editor.Document.Selection;

        var lineRange = sel.GetClone();
        lineRange.Expand(TextRangeUnit.Line);
        int lineStart = lineRange.StartPosition;

        lineRange.GetText(TextGetOptions.None, out string fullLineText);

        bool lineHasBreak = fullLineText.Length > 0 && fullLineText[fullLineText.Length - 1] == '\r';
        string lineContent = lineHasBreak
            ? fullLineText.Substring(0, fullLineText.Length - 1)
            : fullLineText;

        int contentEnd = lineStart + lineContent.Length;

        if (!CalcService.TryEvaluate(lineContent, out string result))
        {
            return;
        }

        bool hasSpaceBefore = lineContent.EndsWith(" =");
        string separator = hasSpaceBefore ? " " : "";
        string newLineContent = lineContent + separator + result;

        isCalcReplacing = true;
        try
        {
            var contentRange = editor.Document.GetRange(lineStart, contentEnd);
            contentRange.SetText(TextSetOptions.None, newLineContent);
            int newCaretPos = lineStart + newLineContent.Length;
            editor.Document.Selection.SetRange(newCaretPos, newCaretPos);
        }
        finally
        {
            isCalcReplacing = false;
        }

        int highlightStart = lineStart + lineContent.Length + separator.Length;
        int highlightEnd = lineStart + newLineContent.Length;
        StartResultAnimation(editor, highlightStart, highlightEnd);
    }

    private void StartResultAnimation(RichEditBox editor, int resultStart, int resultEnd)
    {
        CancelAnimation();

        animationAccentColor = (Color)Application.Current.Resources["SystemAccentColor"];
        bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        animationNormalColor = isDark
            ? Color.FromArgb(255, 32, 32, 32)
            : Color.FromArgb(255, 242, 242, 242);

        animationEditor = editor;
        animationResultStart = resultStart;
        animationResultEnd = resultEnd;
        animationStartTime = DateTime.UtcNow;

        ApplyAnimationColor(animationAccentColor);

        editor.Document.GetText(TextGetOptions.UseCrlf, out string textAfterRewrite);
        animationExpectedTextLength = textAfterRewrite.Length;

        animationTimer = new DispatcherTimer();
        animationTimer.Interval = TimeSpan.FromMilliseconds(AnimationTickMs);
        animationTimer.Tick += OnAnimationTick;
        animationTimer.Start();
    }

    private void OnAnimationTick(object sender, object e)
    {
        double elapsed = (DateTime.UtcNow - animationStartTime).TotalMilliseconds;
        double t = Math.Min(elapsed / AnimationDurationMs, 1.0);

        if (t >= 1.0)
        {
            animationTimer.Stop();
            animationTimer.Tick -= OnAnimationTick;
            animationTimer = null;
            ClearAnimationColor();
            animationEditor = null;
        }
        else
        {
            ApplyAnimationColor(InterpolateColor(animationAccentColor, animationNormalColor, t));
        }
    }

    private void ApplyAnimationColor(Color color)
    {
        if (animationEditor == null)
        {
            return;
        }

        IsApplyingColor = true;
        try
        {
            var range = animationEditor.Document.GetRange(animationResultStart, animationResultEnd);
            range.CharacterFormat.BackgroundColor = color;
        }
        finally
        {
            IsApplyingColor = false;
        }
    }

    private void ClearAnimationColor()
    {
        if (animationEditor == null)
        {
            return;
        }

        IsApplyingColor = true;
        try
        {
            var range = animationEditor.Document.GetRange(animationResultStart, animationResultEnd);
            // AutoColor clears CFM_BACKCOLOR entirely so RichEdit uses its own background,
            // rather than overpainting with a sampled color that may not match after a theme change.
            range.CharacterFormat.BackgroundColor = TextConstants.AutoColor;
        }
        finally
        {
            IsApplyingColor = false;
        }
    }

    private static Color InterpolateColor(Color from, Color to, double t)
    {
        return Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * t),
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }
}
