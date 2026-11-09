using System;
using System.Collections.Generic;
using System.Text.Json;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The search half of the editor bridge (<c>Docs/Specs/24-SEARCH.md § Bridge</c>): the query, steps
/// and close going to the page, and results and tab-switch requests coming back.
/// </summary>
/// <remarks>
/// The query is treated as buffer text — it is a fragment of the user's notes — so it is never
/// logged on either side. Only message names, counts and the tier may be logged.
/// </remarks>
internal sealed class EditorSearchChannel
{
    /// <summary>Incoming message: per-slate counts, the tier, and the current match.</summary>
    public const string ResultsMessage = "searchResults";

    /// <summary>Incoming message: the next match is in another slate; the host should select it.</summary>
    public const string NavigateMessage = "searchNavigate";

    private const string DirectionNext = "next";
    private const string DirectionPrevious = "prev";

    private readonly Action<Action<Utf8JsonWriter>> post;

    /// <summary>Raised when the page reports search results.</summary>
    public event EventHandler<EditorSearchResultsEventArgs> ResultsReceived;

    /// <summary>Raised when the page asks the host to select the tab of 1-based buffer index.</summary>
    public event EventHandler<int> NavigateRequested;

    /// <summary>Creates the channel over the host's message poster.</summary>
    public EditorSearchChannel(Action<Action<Utf8JsonWriter>> post)
    {
        if (post == null)
        {
            throw new ArgumentNullException(nameof(post));
        }

        this.post = post;
    }

    /// <summary>
    /// Handles an incoming message if it belongs to search. Returns <c>false</c> for any other type.
    /// </summary>
    public bool TryReceive(string type, JsonElement root)
    {
        if (type == ResultsMessage)
        {
            ResultsReceived?.Invoke(this, ReadResults(root));
            return true;
        }

        if (type == NavigateMessage)
        {
            NavigateRequested?.Invoke(this, EditorBridgeJson.GetInt(root, "index"));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sends the query (empty clears), the tab strip's buffer order, and the top scroll margin in CSS
    /// px that keeps a scrolled-to match clear of the pill.
    /// </summary>
    public void SendQuery(string query, IReadOnlyList<int> tabOrder, double scrollMarginTop)
    {
        post(writer =>
        {
            writer.WriteString("type", "searchQuery");
            writer.WriteString("query", query ?? string.Empty);
            writer.WriteStartArray("tabOrder");
            foreach (int index in tabOrder)
            {
                writer.WriteNumberValue(index);
            }

            writer.WriteEndArray();
            writer.WriteNumber("scrollMarginTop", scrollMarginTop);
        });
    }

    /// <summary>
    /// Steps to the next or previous match. <paramref name="fromCaret"/> discards the current match
    /// first, so the step starts at the editor's caret (F3 reopening a closed search).
    /// </summary>
    public void SendStep(bool forward, bool fromCaret)
    {
        post(writer =>
        {
            writer.WriteString("type", "searchStep");
            writer.WriteString("direction", forward ? DirectionNext : DirectionPrevious);
            writer.WriteBoolean("fromCaret", fromCaret);
        });
    }

    /// <summary>Ends the search, optionally turning the current match into the selection.</summary>
    public void SendClose(bool selectCurrent)
    {
        post(writer =>
        {
            writer.WriteString("type", "searchClose");
            writer.WriteBoolean("selectCurrent", selectCurrent);
        });
    }

    private static EditorSearchResultsEventArgs ReadResults(JsonElement root)
    {
        var counts = new List<int>();
        if (root.TryGetProperty("counts", out JsonElement countsElement) && countsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement count in countsElement.EnumerateArray())
            {
                counts.Add(count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0);
            }
        }

        var capped = new List<bool>();
        if (root.TryGetProperty("capped", out JsonElement cappedElement) && cappedElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement flag in cappedElement.EnumerateArray())
            {
                capped.Add(flag.ValueKind == JsonValueKind.True);
            }
        }

        int currentIndex = 0;
        int currentOrdinal = 0;
        if (root.TryGetProperty("current", out JsonElement current) && current.ValueKind == JsonValueKind.Object)
        {
            currentIndex = EditorBridgeJson.GetInt(current, "index");
            currentOrdinal = EditorBridgeJson.GetInt(current, "ordinal");
        }

        return new EditorSearchResultsEventArgs(counts, capped, EditorBridgeJson.GetInt(root, "tier"), currentIndex, currentOrdinal);
    }
}
