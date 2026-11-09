// Editor surface styling lives in a CodeMirror theme (not plain CSS) so it reliably wins over
// CM6's injected base theme: Cascadia Code 15px, line-height 1.4. The retired RichEditBox padding
// (16/10/16/16 plus the 4px content top gap) is now a WebView2 margin, not content padding, so it
// cannot scroll away. Colours read the CSS variables the host sets via the theme message.

import { EditorView } from "@codemirror/view";

export const editorTheme = EditorView.theme({
  "&": {
    height: "100%",
    backgroundColor: "transparent",
    color: "var(--text)",
  },
  "&.cm-focused": { outline: "none" },
  ".cm-scroller": {
    fontFamily: "'Cascadia Code', 'Cascadia Mono', Consolas, monospace",
    fontSize: "15px",
    // The RichEditBox used LineSpacingRule.Multiple 1.4, which multiplies the font's natural line
    // height (~1.2em); CSS line-height multiplies the em directly, so ~1.6 matches that spacing.
    lineHeight: "1.6",
    overflowX: "hidden",
    // Chromium's ::-webkit-scrollbar is a classic scrollbar: it consumes layout width the moment the
    // buffer becomes scrollable, which would shrink the content box (and the selection's right edge)
    // by SCROLLBAR_WIDTH_PX. Reserving the gutter always keeps the text inset identical whether or
    // not a scrollbar is showing. The WebView2's right margin is narrowed by the same amount so the
    // reserved gutter sits inside the edge gap instead of adding to it.
    scrollbarGutter: "stable",
  },
  ".cm-content": {
    padding: "0",
    // Room for the open search pill (searchSpacer.js). Unlike the fixed insets it is meant to
    // scroll away, so it is content padding rather than a WebView2 margin; 0 while search is shut.
    // The minimum height grows with it so a slate too short to scroll can still scroll it away.
    paddingTop: "var(--search-spacer, 0px)",
    minHeight: "calc(100% + var(--search-spacer, 0px))",
    caretColor: "var(--caret)",
    // CM6 sets no cursor, and the UA default (auto) only resolves to an I-beam where there is text
    // under the pointer: the padding strip, the marker widgets, and the space past the end of a
    // line would all show an arrow. It is all editor surface, so say so once here and let it
    // inherit; the checkbox and links override it back to a pointer on themselves.
    cursor: "text",
  },
  ".cm-line": {
    // The editor's only horizontal inset, and only because it cannot scroll away: the page never
    // scrolls horizontally (overflow-x: hidden on the scroller), so this padding is fixed chrome
    // the way the WebView2's other three margins are. Vertical padding would scroll (it lives
    // inside the scrolled region), which is why the top and bottom insets stay margins. It is
    // padding so that the strip between the window edge and the text belongs to the editor:
    // clicking it keeps focus and puts the caret at the start of that line, where a XAML margin
    // would have handed the click to the panel and blanked the caret. It matches the WebView2's
    // retired 16px left margin (BufferPanel.xaml), so the text inset is unchanged.
    //
    // On the line and NOT on .cm-content, even though both inset the text identically. CM6 draws
    // selection rectangles from `contentRect.left + parseInt(getComputedStyle(.cm-line).paddingLeft)`
    // (rectanglesForRange in @codemirror/view): it reads the *line's* padding and the *content's*
    // border box, so an inset owned by .cm-content is invisible to that measurement and every
    // full-line selection band was drawn flush against the window edge with no margin.
    padding: "0 0 0 16px",
  },
  ".cm-cursor, .cm-dropCursor": {
    borderLeftColor: "var(--caret)",
    // CM6's base theme offsets the caret by margin-left: -0.6px to straddle the character boundary.
    // .cm-cursorLayer is a direct child of .cm-scroller, which clips at overflow-x: hidden, so a
    // column-0 caret put half its 1.2px border outside the clip and all but disappeared. The
    // line's left padding now holds it clear of that edge anyway; it stays flush inside the
    // character boundary because the 0.6px shift is imperceptible either way.
    marginLeft: "0",
  },
  ".cm-selectionBackground": { background: "var(--selection)" },
  // The focused rule must mirror CM6's base-theme selector shape exactly. Its
  // `&light.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground` scores (0,5,0);
  // a shorter `&.cm-focused .cm-selectionBackground` scores (0,3,0) and loses, so the base theme's
  // own selection colour would show whenever the editor has focus — i.e. always. Matching the shape
  // ties the specificity, and the base theme is Prec.lowest, so ours is mounted later and wins.
  "&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground": {
    background: "var(--selection)",
  },
});
