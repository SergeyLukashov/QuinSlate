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
    // No padding at all: every edge inset is a margin on the WebView2 element, so it stays put
    // during scroll (CM6 content padding lives inside the scrolled region and scrolls away).
    padding: "0",
    caretColor: "var(--caret)",
  },
  ".cm-line": { padding: "0" },
  ".cm-cursor, .cm-dropCursor": {
    borderLeftColor: "var(--caret)",
    // CM6's base theme offsets the caret by margin-left: -0.6px to straddle the character boundary.
    // .cm-cursorLayer is a direct child of .cm-scroller, which clips at overflow-x: hidden, so with
    // no content padding a column-0 caret puts half its 1.2px border outside the clip and all but
    // disappears. Sit it flush inside the boundary instead; the 0.6px shift is imperceptible.
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
