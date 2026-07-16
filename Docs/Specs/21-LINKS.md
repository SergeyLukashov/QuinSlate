# Clickable links

> _Last updated: 2026-07-16_

The third formatting feature of the CodeMirror editor, alongside
[checkable tasks](19-CHECKABLE-TASKS.md) and [lists](20-LISTS.md): a URL in a
buffer renders in the Windows accent colour and opens in the user's browser or
mail client on click. No toolbar, no link dialog, no cards or previews.

Unlike tasks and lists, this feature **writes nothing to the document**. A link
is a mark decoration over the URL's own characters, so the text stays plain
editable text and the buffer `.txt` is byte-for-byte what it would be without
the feature. Detection and rendering live page-side
(`QuinSlate.Ui/WebEditor/build/src/links.js` + `editor.css`); opening is the
host's job, because the page can never navigate itself.

## What is a link

- A run of non-whitespace characters carrying an **explicit scheme** —
  `http://`, `https://`, or `mailto:`. The scheme match is case-insensitive.
- **Nothing else.** A bare `example.com`, a `www.` host, and a bare
  `someone@example.com` are text. Requiring the scheme is what keeps a
  scratchpad's `node.js`, `README.md`, `main.rs`, and `v1.2` from turning blue —
  a bare-domain pattern claims all of them, and a false positive in a notes app
  is worse than a URL the user has to click twice.
- The scheme must start at a word boundary, so `xhttps://example.com` is not a
  link.
- Recognition applies anywhere a URL appears — typed, loaded from disk, pasted,
  or restored by undo — and re-runs from the line's text on every change. There
  is no link state outside the document.

### Where a link ends

The URL runs to the next whitespace, then trailing characters that end a
sentence rather than a URL are dropped:

| Text | Link |
|---|---|
| `Go to https://example.com.` | `https://example.com` — the period is the sentence's |
| `(see https://example.com)` | `https://example.com` — the paren is the sentence's |
| `https://en.wikipedia.org/wiki/Foo_(bar)` | the whole thing — the URL opened that paren itself |
| `<https://example.com>` | `https://example.com` |
| `https://example.com/x/report.pdf` | the whole thing — a trailing `f`, not punctuation |

Trailing `.,;:!?'"` always drop. A closing `)`, `]`, `}`, or `>` drops only when
the candidate does not open it too, so a Wikipedia URL survives and a
parenthesised aside does not keep its paren.

## Behaviour

| Action | Result |
|---|---|
| Click a link | Opens it in the default browser (or mail client for `mailto:`); the caret still lands where it was clicked |
| Drag across a link | Selects text as usual — no open |
| Shift+click into a link | Extends the selection as usual — no open |
| Type / arrow / Backspace inside a link | Ordinary text editing; the decoration re-computes as the text changes |
| Right-click a link | The shared editor context menu, unchanged (no link-specific items) |

Plain click opens, rather than Ctrl+click. A URL is the one run of text in a
scratchpad the user almost never wants to edit character-by-character, and the
caret still lands where it was clicked, so the link remains editable — click
into it, then type.

Because opening a link gives the browser focus, an unpinned panel auto-hides
behind it, exactly as it does for any other focus loss. Pin the window
([09-PIN-WINDOW.md](09-PIN-WINDOW.md)) to keep it up.

## Rendering

- No widget, no replacement, no hidden characters: the user sees the URL's own
  text, coloured.
- The underline appears **on hover only**, where the pointer cursor is already
  signalling the click target. A permanent underline turns a buffer of links
  into a wall of underscores.

### Colour

`.cm-link` uses `var(--link)` — **not** `var(--accent)`. The two are different
colours on purpose:

| Variable | Source | Used for |
|---|---|---|
| `--accent` | the raw `SystemAccentColor` | accent as a **fill**, sitting behind content: the task checkbox, the selection, the calc highlight |
| `--link` | the raw accent, shaded for legibility and contrast-checked | accent as **foreground text** |

The raw accent is a mid-tone. Behind a white checkmark it is exactly right; in
front of the gradient it is too dim to read — measured at **3.25:1** against the
dark mesh (`#292826`) and **4.26:1** against the light one (`#F9F8F4`), both
under the 4.5:1 WCAG AA floor for normal text. WinUI draws the same distinction:
`AccentFillColorDefaultBrush` is the raw accent, while
`AccentTextFillColorPrimaryBrush` — what every `HyperlinkButton` uses — resolves
to `SystemAccentColorLight3` on dark and `SystemAccentColorDark2` on light.

`Services/AccentTextColorResolver` derives `--link` in two steps:

1. **Take the OS shade for the theme** — `UIColorType.AccentLight3` on dark,
   `AccentDark2` on light: the same shades behind `AccentTextFillColorPrimaryBrush`,
   so a link matches every other Windows app's link, and the user's accent choice
   is respected. For the stock blue this is 11.0:1 on dark and 9.4:1 on light.
2. **Verify it against the real background** — the gradient's flat mid-tone
   (`DitheredGradientBrushFactory.MidColor`) — and, only if it still falls under
   4.5:1, blend it toward white (dark) or black (light) by the smallest amount
   that clears the bar.

Step 2 exists because step 1 is not a guarantee. The shade-generation algorithm
is unpublished, differs between Windows 10 and 11
([microsoft-ui-xaml#6119](https://github.com/microsoft/microsoft-ui-xaml/issues/6119),
closed as not planned), and was tuned against stock WinUI surfaces rather than
this app's gradient mesh. Microsoft's own
[theming guidance](https://learn.microsoft.com/windows/apps/develop/ui/theming)
tells developers to check accent-text contrast themselves. A near-black custom
accent would otherwise produce an unreadable link, so readability is made a
property of the code instead of a hope about the user's colour picker.

Both are read from `UISettings` rather than the XAML resources, for the reason
`ReadAccentColor` already gives: `ColorValuesChanged` can fire before XAML has
refreshed its theme resources. Both are re-sent on accent **and** theme change.

## Opening (host side)

1. The page posts `openLink` with the href over the bridge.
2. `Services/LinkService.TryCreateLaunchUri` re-validates it: it must parse as an
   **absolute** URI whose scheme is http, https, or mailto. Anything else —
   `file:`, `javascript:`, `ms-settings:`, a relative reference, a malformed
   string — is declined and logged as a decline.
3. `Components/EditorHost` hands the URI to `Windows.System.Launcher.LaunchUriAsync`.

The page already filters to those three schemes; the host re-checks because the
href arrives over the bridge as buffer text. `EditorHost` cancels every
navigation off the virtual host, so the WebView2 itself can never follow a link.

**The href is buffer content: it is never logged**, on either side, including on
a failed launch — only the outcome and the scheme.

## Non-goals (for now)

- No `www.` or bare-domain detection (see What is a link), and no bare email
  addresses — `mailto:` is required.
- No link titles, labels, or `[text](url)` markdown syntax — the URL is the text.
- No cards, previews, favicons, or hover tooltips showing the target.
- No Ctrl+click, no "Open link" / "Copy link" context-menu items.
- No visited-link state and no `file:` / UNC path linking.
- **No High Contrast handling.** WinUI's HC dictionary discards the accent
  entirely (`AccentTextFillColorPrimary` → `SystemColorWindowTextColor`), but the
  editor surface as a whole does not adapt to HC yet — the gradient mesh and the
  editor text colour are both picked from `ActualTheme` alone. Links inherit that
  gap rather than solving it locally; fixing it belongs to the whole surface.
