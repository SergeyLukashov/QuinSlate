using QuinSlate.Ui.Services;
using System.Collections.Generic;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Tray;

/// <summary>
/// Builds the display rows for the tray peek window from live buffer and tab state.
/// </summary>
internal static class TrayPeekRowBuilder
{
    private const string EmptyPreview = "(empty)";

    /// <summary>
    /// Returns one <see cref="TrayPeekRow"/> per tab, in the same left-to-right order the tab
    /// strip shows (the user drag-reorders tabs, and settings stores that order), combining the
    /// tab emoji and title from <paramref name="settingsService"/> with the first content line of
    /// that tab's buffer from <paramref name="bufferService"/>.
    /// </summary>
    public static TrayPeekRow[] Build(BufferService bufferService, SettingsService settingsService)
    {
        IReadOnlyList<QuinSlate.Ui.Models.TabDefinition> tabs = settingsService.GetTabs();
        var rows = new TrayPeekRow[tabs.Count];

        for (int slot = 0; slot < tabs.Count; slot++)
        {
            QuinSlate.Ui.Models.TabDefinition tab = tabs[slot];
            Buffer buffer = bufferService.GetBuffer(tab.Id);
            string content = buffer != null ? buffer.Content : string.Empty;

            // The row number is the tab's position, matching the Ctrl+1..Ctrl+5 shortcuts (which
            // select by position), not the buffer id behind it.
            int number = slot + 1;

            if (string.IsNullOrEmpty(content))
            {
                rows[slot] = new TrayPeekRow(number, tab.Emoji, tab.Title, EmptyPreview, true);
            }
            else
            {
                string firstLine = content;
                int newlineIndex = content.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIndex >= 0)
                {
                    firstLine = content.Substring(0, newlineIndex);
                }

                rows[slot] = new TrayPeekRow(number, tab.Emoji, tab.Title, firstLine, false);
            }
        }

        return rows;
    }
}
