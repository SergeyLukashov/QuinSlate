using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Carries a right-click from the editor page: the click position (in the page's CSS pixels,
/// which equal DIPs for the WebView2 element) plus the command-enablement state the host uses to
/// configure the shared context menu.
/// </summary>
internal sealed class EditorContextMenuEventArgs : EventArgs
{
    /// <summary>Creates the event args.</summary>
    public EditorContextMenuEventArgs(double x, double y, bool canUndo, bool canRedo, bool hasSelection)
    {
        X = x;
        Y = y;
        CanUndo = canUndo;
        CanRedo = canRedo;
        HasSelection = hasSelection;
    }

    /// <summary>Horizontal click position in DIPs relative to the editor surface.</summary>
    public double X { get; }

    /// <summary>Vertical click position in DIPs relative to the editor surface.</summary>
    public double Y { get; }

    /// <summary>Whether the undo stack is non-empty.</summary>
    public bool CanUndo { get; }

    /// <summary>Whether the redo stack is non-empty.</summary>
    public bool CanRedo { get; }

    /// <summary>Whether a non-empty selection exists.</summary>
    public bool HasSelection { get; }
}
