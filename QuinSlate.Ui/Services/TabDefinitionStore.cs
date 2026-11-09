using QuinSlate.Ui.Models;
using System;
using System.Collections.Generic;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Holds the live tab definitions (each tab's emoji, title, and position) and writes every change
/// back to <c>settings.json</c>.
/// </summary>
internal sealed class TabDefinitionStore
{
    private readonly SettingsService settingsService;
    private IReadOnlyList<TabDefinition> tabDefinitions;

    /// <summary>Creates the store over the definitions <paramref name="settingsService"/> loaded.</summary>
    /// <param name="settingsService">Where changes are persisted.</param>
    /// <param name="tabDefinitions">The definitions in left-to-right order.</param>
    public TabDefinitionStore(SettingsService settingsService, IReadOnlyList<TabDefinition> tabDefinitions)
    {
        if (settingsService == null)
        {
            throw new ArgumentNullException(nameof(settingsService));
        }

        if (tabDefinitions == null)
        {
            throw new ArgumentNullException(nameof(tabDefinitions));
        }

        this.settingsService = settingsService;
        this.tabDefinitions = tabDefinitions;
    }

    /// <summary>The definitions in left-to-right order.</summary>
    public IReadOnlyList<TabDefinition> Tabs => tabDefinitions;

    /// <summary>The definition of buffer <paramref name="bufferIndex"/>, or null when there is none.</summary>
    public TabDefinition Find(int bufferIndex)
    {
        return TabOrder.Find(tabDefinitions, bufferIndex);
    }

    /// <summary>
    /// Writes the strip's live left-to-right order back to <c>settings.json</c>, where the tab
    /// array's order <em>is</em> the tab order, so a reorder survives relaunch. Returns
    /// <c>true</c> only when the strip's order actually differs from the order already held, so
    /// that neither the strip's initial realization nor a drag the user drops where it started
    /// triggers a settings write.
    /// </summary>
    /// <param name="stripBufferIndices">The buffer index of each tab, left to right.</param>
    /// <param name="expectedTabCount">How many tabs a fully populated strip holds.</param>
    public bool TryApplyStripOrder(IReadOnlyList<int> stripBufferIndices, int expectedTabCount)
    {
        List<TabDefinition> reordered = TabOrder.FromStrip(stripBufferIndices, tabDefinitions);
        if (reordered.Count != expectedTabCount || TabOrder.Matches(reordered, tabDefinitions))
        {
            return false;
        }

        tabDefinitions = reordered;
        settingsService.SetTabs(reordered);
        return true;
    }

    /// <summary>Sets buffer <paramref name="bufferIndex"/>'s emoji and title, and persists them.</summary>
    public void UpdateLabel(int bufferIndex, string emoji, string title)
    {
        var updated = new List<TabDefinition>();
        foreach (var td in tabDefinitions)
        {
            if (td.Id == bufferIndex)
            {
                updated.Add(new TabDefinition { Id = td.Id, Emoji = emoji, Title = title });
            }
            else
            {
                updated.Add(new TabDefinition { Id = td.Id, Emoji = td.Emoji, Title = td.Title });
            }
        }

        tabDefinitions = updated;
        settingsService.SetTabs(updated);
    }
}
