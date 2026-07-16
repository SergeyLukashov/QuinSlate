// Clickable links (Docs/Specs/21-LINKS.md). A URL carrying an explicit
// http://, https://, or mailto: scheme is recognised wherever it appears —
// typed, loaded from disk, or pasted — and marked so it renders in the system
// accent and opens on click. Nothing is written to the document: a link is a
// mark decoration over ordinary characters, so it stays editable exactly like
// the text around it and the buffer .txt has no idea it is a link.
//
// The page never opens anything itself (it cannot: EditorHost cancels every
// navigation off the virtual host). A click posts the href to the host, which
// re-validates the scheme and hands the URI to the shell.

import { Decoration, ViewPlugin } from "@codemirror/view";
import { postToHost } from "./hostBridge.js";

const LINK_CLASS = "cm-link";
const HREF_ATTRIBUTE = "data-href";

// A click that moves the pointer this far is a drag — the user is selecting the
// link's text, not following it.
const DRAG_THRESHOLD_PX = 4;

// Explicit schemes only. A bare-domain pattern would also claim "node.js",
// "README.md", and "v1.2" — all of which a scratchpad is full of.
const LINK_PATTERN = /\b(?:https?:\/\/|mailto:)[^\s]+/gi;

// A candidate still has to carry something past its scheme once the trailing
// punctuation is off, or "https://." would leave a bare "https://" behind.
const COMPLETE_LINK = /^(?:https?:\/\/|mailto:)[^\s]+$/i;

const TRAILING_PUNCTUATION = ".,;:!?'\"";
const CLOSING_BRACKETS = { ")": "(", "]": "[", "}": "{", ">": "<" };

function countOf(text, char) {
  let count = 0;
  for (let i = 0; i < text.length; i++) {
    if (text[i] === char) {
      count++;
    }
  }
  return count;
}

// "(see https://example.com)." ends at the domain: the period and paren close
// the sentence, not the URL. But the parens of
// "https://en.wikipedia.org/wiki/Foo_(bar)" are the URL's own, so a closing
// bracket survives when the candidate opens it too.
function trimTrailing(candidate) {
  let end = candidate.length;
  while (end > 0) {
    const char = candidate[end - 1];
    if (TRAILING_PUNCTUATION.indexOf(char) >= 0) {
      end--;
      continue;
    }
    const opener = CLOSING_BRACKETS[char];
    if (opener != null) {
      const head = candidate.slice(0, end);
      if (countOf(head, opener) < countOf(head, char)) {
        end--;
        continue;
      }
    }
    break;
  }
  return candidate.slice(0, end);
}

// Every link in one line of text, as offsets into that line.
export function findLinks(text) {
  const links = [];
  LINK_PATTERN.lastIndex = 0;
  let match = LINK_PATTERN.exec(text);
  while (match != null) {
    const href = trimTrailing(match[0]);
    if (COMPLETE_LINK.test(href)) {
      links.push({ from: match.index, to: match.index + href.length, href });
    }
    match = LINK_PATTERN.exec(text);
  }
  return links;
}

function buildLinkDecorations(view) {
  const marks = [];
  for (const range of view.visibleRanges) {
    let pos = range.from;
    while (pos <= range.to) {
      const line = view.state.doc.lineAt(pos);
      for (const link of findLinks(line.text)) {
        marks.push(
          Decoration.mark({
            class: LINK_CLASS,
            attributes: { [HREF_ATTRIBUTE]: link.href },
          }).range(line.from + link.from, line.from + link.to)
        );
      }
      pos = line.to + 1;
    }
  }
  return Decoration.set(marks);
}

let mouseDownAt = null;

function recordMouseDown(event) {
  mouseDownAt = { x: event.clientX, y: event.clientY, shift: event.shiftKey };
  return false;
}

// A plain click opens the link; the caret still lands where it was clicked, so
// the link is as editable as anything else on the line. Shift+click (extending
// a selection into the link) and a drag (selecting the link's text) are the
// user reaching for the text, and are left to CM6's own mouse handling. The
// pointer coordinates decide this rather than the resulting selection: CM6
// flushes its DOM-selection reads on its own schedule, so state.selection is
// not reliably settled by the time this click fires.
function openLinkFromEvent(event) {
  const origin = mouseDownAt;
  mouseDownAt = null;
  if (origin == null || origin.shift) {
    return false;
  }
  if (
    Math.abs(event.clientX - origin.x) > DRAG_THRESHOLD_PX ||
    Math.abs(event.clientY - origin.y) > DRAG_THRESHOLD_PX
  ) {
    return false;
  }
  const target = event.target;
  if (!(target instanceof Element)) {
    return false;
  }
  const link = target.closest("." + LINK_CLASS);
  if (link == null) {
    return false;
  }
  const href = link.getAttribute(HREF_ATTRIBUTE);
  if (href == null) {
    return false;
  }
  // The href is buffer content; it crosses the bridge but is never logged.
  postToHost("openLink", { href });
  return true;
}

export const linkPlugin = ViewPlugin.fromClass(
  class {
    constructor(view) {
      this.decorations = buildLinkDecorations(view);
    }

    update(update) {
      if (update.docChanged || update.viewportChanged) {
        this.decorations = buildLinkDecorations(update.view);
      }
    }
  },
  {
    decorations: (value) => value.decorations,
    eventHandlers: {
      mousedown: recordMouseDown,
      click: openLinkFromEvent,
    },
  }
);
