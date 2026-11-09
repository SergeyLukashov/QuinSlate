namespace QuinSlate.Ui.Components;

/// <summary>What the search pill's counter shows, and what a screen reader hears instead.</summary>
internal sealed class SearchCounterText
{
    /// <summary>Creates the counter text.</summary>
    public SearchCounterText(string display, string accessible, string toolTip, bool isMuted, bool canStep)
    {
        Display = display;
        Accessible = accessible;
        ToolTip = toolTip;
        IsMuted = isMuted;
        CanStep = canStep;
    }

    /// <summary>The counter's visible text, e.g. <c>2 of 6</c>.</summary>
    public string Display { get; }

    /// <summary>The text announced through the live region; spells out what <c>≈</c> means.</summary>
    public string Accessible { get; }

    /// <summary>The counter's tooltip, or <c>null</c> for none.</summary>
    public string ToolTip { get; }

    /// <summary>Whether the counter is drawn muted (nothing matched).</summary>
    public bool IsMuted { get; }

    /// <summary>Whether the previous/next buttons have anywhere to go.</summary>
    public bool CanStep { get; }
}
