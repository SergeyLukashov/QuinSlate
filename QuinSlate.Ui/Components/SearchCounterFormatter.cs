using System.Globalization;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Words the search pill's counter (<c>Docs/Specs/24-SEARCH.md § The counter</c>): <c>2 of 6</c>,
/// <c>6 results</c> when the active tab has none yet, a leading <c>≈</c> for close matches, and a
/// calm <c>No results</c>.
/// </summary>
internal static class SearchCounterFormatter
{
    /// <summary>The tier of close (typo-tolerant) matches.</summary>
    public const int CloseTier = 3;

    private const string NoResultsText = "No results";
    private const string CloseMarker = "≈ ";
    private const string CappedSuffix = "+";
    private const string CloseToolTip = "No exact matches — showing close ones";
    private const string CloseMatchesSuffix = " close matches";
    private const string CloseMatchSuffix = " close match";
    private const string ResultsSuffix = " results";
    private const string ResultSuffix = " result";
    private const string CountFormat = "N0";
    private const string OfSeparator = " of ";

    /// <summary>Formats the counter for a search's results.</summary>
    /// <param name="total">Matches across every slate.</param>
    /// <param name="anyCapped">Whether any slate stopped counting at the cap.</param>
    /// <param name="tier">The tier the matches came from; 0 when nothing matched.</param>
    /// <param name="ordinal">The current match's 1-based position; 0 when there is no current match.</param>
    public static SearchCounterText Format(int total, bool anyCapped, int tier, int ordinal)
    {
        if (total <= 0)
        {
            return new SearchCounterText(NoResultsText, NoResultsText, null, isMuted: true, canStep: false);
        }

        string totalText = total.ToString(CountFormat, CultureInfo.CurrentCulture) + (anyCapped ? CappedSuffix : string.Empty);
        bool isClose = tier == CloseTier;

        string counted;
        string accessible;
        if (ordinal > 0)
        {
            counted = ordinal.ToString(CountFormat, CultureInfo.CurrentCulture) + OfSeparator + totalText;
            accessible = isClose ? counted + CloseMatchesSuffix : counted;
        }
        else
        {
            bool single = total == 1 && anyCapped == false;
            counted = totalText + (single ? ResultSuffix : ResultsSuffix);
            accessible = isClose ? totalText + (single ? CloseMatchSuffix : CloseMatchesSuffix) : counted;
        }

        return new SearchCounterText(
            isClose ? CloseMarker + counted : counted,
            accessible,
            isClose ? CloseToolTip : null,
            isMuted: false,
            canStep: true);
    }
}
