using Jott.Ui.Services;
using System.Collections.Generic;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Tray;

/// <summary>
/// Builds the display rows for the tray peek window from live buffer and tab state.
/// </summary>
internal static class TrayPeekRowBuilder
{
    private const string EmptyPreview = "(empty)";

    /// <summary>
    /// Returns one <see cref="TrayPeekRow"/> per buffer, combining the tab
    /// emoji and title from <paramref name="settingsService"/> with the first
    /// content line from <paramref name="bufferService"/>.
    /// </summary>
    public static TrayPeekRow[] Build(BufferService bufferService, SettingsService settingsService)
    {
        var tabs = settingsService.GetTabs();
        var tabById = new Dictionary<int, Jott.Ui.Models.TabDefinition>(tabs.Count);
        foreach (var tab in tabs)
        {
            tabById[tab.Id] = tab;
        }

        int count = Buffer.MaxIndex - Buffer.MinIndex + 1;
        var rows = new TrayPeekRow[count];

        for (int i = Buffer.MinIndex; i <= Buffer.MaxIndex; i++)
        {
            int slot = i - Buffer.MinIndex;
            var buffer = bufferService.GetBuffer(i);
            string content = buffer != null ? buffer.Content : string.Empty;

            Jott.Ui.Models.TabDefinition tab;
            tabById.TryGetValue(i, out tab);
            string emoji = tab != null ? tab.Emoji : i.ToString();
            string title = tab != null ? tab.Title : i.ToString();
            string label = emoji + " " + title;

            if (string.IsNullOrEmpty(content))
            {
                rows[slot] = new TrayPeekRow(label, EmptyPreview, true);
            }
            else
            {
                string firstLine = content;
                int newlineIndex = content.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIndex >= 0)
                {
                    firstLine = content.Substring(0, newlineIndex);
                }

                rows[slot] = new TrayPeekRow(label, firstLine, false);
            }
        }

        return rows;
    }
}
