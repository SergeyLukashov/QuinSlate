namespace QuinSlate.Ui.Constants;

/// <summary>
/// Contains the default engaging text content for the five buffers.
/// </summary>
public static class DefaultBuffers
{
    private const string Tab1 =
        "Five tabs, no cloud, no save button. Your words never leave your machine.\r\n" +
        "\r\n" +
        "Ctrl+Shift+Q       Makes the app vanish and reappear from anywhere\r\n" +
        "Ctrl+Tab(+Shift)   Next(previous) tab\r\n" +
        "Ctrl+1…5           Jump to a tab\r\n" +
        "\r\n" +
        "It also does math. Try it. End the line below with '='\r\n" +
        "2 * (13 + 8) \r\n" +
        "\r\n" +
        "Rename any tab. Swap any emoji. But there are five, and there will only ever be five. Five fits in your head. That's the point.\r\n" +
        "\r\n" +
        "This is Scratch — the one is fine to wreck. Now wreck it.\r\n";

    private const string Tab2 =
        "What needs doing. No reason it has to be boring.\r\n" +
        "\r\n" +
        "- Crank up the techno and aggressively dust the blinds\r\n" +
        "- Play dishwasher Tetris to fit every single dirty plate\r\n" +
        "- Sweep the kitchen floor while moonwalking like a pro\r\n" +
        "\r\n" +
        "See the pin up top? Flip it — the panel floats over everything while you work. Unpin when it's in the way.\r\n";

    private const string Tab3 =
        "Half-formed thoughts, before they evaporate.\r\n" +
        "\r\n" +
        "Throw them here fast and ugly. The good ones survive.\r\n" +
        "Delete the rest without a second thought.\r\n" +
        "\r\n" +
        "This is why there are only five tabs: six becomes twelve, then forty, then a folder you never open again.\r\n";

    private const string Tab4 =
        "Links for the next hour, or forever. Your take.\r\n" +
        "\r\n" +
        "- https://example.com\r\n" +
        "- the PR link\r\n" +
        "- the doc everyone keeps asking for\r\n" +
        "- that thing from the call\r\n" +
        "\r\n" +
        "Hover the tray icon: you'll see the first line of every tab.\r\n" +
        "So write good first lines. A first line is a label.\r\n";

    private const string Tab5 =
        "The stuff worth keeping a little longer.\r\n" +
        "\r\n" +
        "Close the app. Restart Windows. Pull the plug.\r\n" +
        "It's all still here when you toggle back in.\r\n" +
        "\r\n" +
        "That's the tour. Five tabs, one hotkey, zero ceremony.\r\n" +
        "Now delete all of this and make it yours.\r\n";

    /// <summary>
    /// Gets the default content for the buffer at the specified 1-based index.
    /// </summary>
    /// <param name="index">The 1-based index of the buffer.</param>
    /// <returns>The default text content, or an empty string if the index is out of range.</returns>
    public static string GetDefaultContent(int index)
    {
        switch (index)
        {
            case 1:
                return Tab1;
            case 2:
                return Tab2;
            case 3:
                return Tab3;
            case 4:
                return Tab4;
            case 5:
                return Tab5;
            default:
                return string.Empty;
        }
    }
}
