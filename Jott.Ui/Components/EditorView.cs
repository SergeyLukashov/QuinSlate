using Microsoft.UI.Xaml.Controls;

namespace Jott.Ui.Components;

/// <summary>
/// Holds references to the named sub-elements that make up a single buffer
/// editor surface. Produced by <see cref="EditorViewBuilder"/> and consumed
/// by the buffer panel which wires events on the parts.
/// </summary>
internal sealed class EditorView
{
    /// <summary>The outer <see cref="Grid"/> hosting the editor and the overlay buttons. Carries a <c>TranslateTransform</c> used by the active-content entrance animation.</summary>
    public Grid Container { get; set; }

    /// <summary>The monospace <see cref="RichEditBox"/> bound to the buffer's content.</summary>
    public RichEditBox Editor { get; set; }
}
