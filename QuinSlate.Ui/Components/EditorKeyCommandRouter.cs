using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Routes the panel shortcuts the CodeMirror keymap forwards over the bridge (keys typed inside the
/// WebView2 never reach XAML) to the same handlers the panel's own <c>PreviewKeyDown</c> uses.
/// </summary>
internal sealed class EditorKeyCommandRouter
{
    // Panel-shortcut command names forwarded from the CodeMirror keymap over the bridge.
    private const string KeyCommandCycleNext = "cycle-next";
    private const string KeyCommandCyclePrev = "cycle-prev";
    private const string KeyCommandSelectPrefix = "select-";
    private const string KeyCommandEditFlyout = "edit-flyout";
    private const string KeyCommandFindNext = "find-next";
    private const string KeyCommandFindPrev = "find-prev";
    private const string KeyCommandFindClose = "find-close";

    private const int CycleForward = 1;
    private const int CycleBackward = -1;

    private readonly BufferKeyboardController keyboardController;
    private readonly SearchController searchController;

    /// <summary>Creates the router.</summary>
    /// <param name="keyboardController">Handles the tab shortcuts.</param>
    /// <param name="searchController">Handles the search shortcuts.</param>
    public EditorKeyCommandRouter(BufferKeyboardController keyboardController, SearchController searchController)
    {
        if (keyboardController == null)
        {
            throw new ArgumentNullException(nameof(keyboardController));
        }

        this.keyboardController = keyboardController;
        this.searchController = searchController;
    }

    /// <summary>Runs the shortcut named <paramref name="command"/>; an unknown name is ignored.</summary>
    public void Route(string command)
    {
        if (command == KeyCommandCycleNext)
        {
            keyboardController.CycleBuffer(CycleForward);
        }
        else if (command == KeyCommandCyclePrev)
        {
            keyboardController.CycleBuffer(CycleBackward);
        }
        else if (command == KeyCommandEditFlyout)
        {
            keyboardController.RequestEditFlyout();
        }
        else if (command == KeyCommandFindNext || command == KeyCommandFindPrev)
        {
            searchController?.Step(command == KeyCommandFindNext);
        }
        else if (command == KeyCommandFindClose)
        {
            searchController?.Close(selectCurrent: true);
        }
        else if (command.StartsWith(KeyCommandSelectPrefix, StringComparison.Ordinal))
        {
            if (int.TryParse(command.Substring(KeyCommandSelectPrefix.Length), out int oneBased))
            {
                keyboardController.SelectBuffer(oneBased - 1);
            }
        }
    }
}
