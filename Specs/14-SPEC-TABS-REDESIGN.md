# Spec #14 — Tab System Redesign

## Overview

Replace the current numbered, colour-coded fixed buffers with a tab bar integrated into
the title bar. Each tab carries a user-editable emoji and title. The five buffers remain
fixed — no add or remove affordance is exposed at any point.

---

## 1. Tab Bar Layout

The tab bar lives **inside the title bar**, using WinUI 3's `AppWindow.TitleBar`
customisation with `ExtendsContentIntoTitleBar = true`. The tab strip occupies the
draggable title area; empty space to the right of the last tab remains a drag region;
the tab labels themselves are not draggable.

- The content area below the tab bar fills **100 % of the remaining window height**
  (`window height − title bar height`). No toolbar row or additional chrome sits between
  the tab strip and the text editor.
- Title bar height follows the **Windows system default** for the current DPI/scale
  setting and is not overridden in code.
- The pin toggle and system caption buttons (minimise, close) remain at the far right of
  the title bar chrome in their standard positions.

---

## 2. Fixed Tab Count

There are exactly **5 tabs**. No tab may be added or removed. The add-tab button (`+`)
and any per-tab close affordance (`×`) are **absent from the UI entirely** — they must
not appear even in a disabled state.

---

## 3. Tab Anatomy

Each tab renders as:

```
[emoji]  [title]
```

- Tab widths are **equal and fixed**, dividing the available title bar width evenly
  across all five tabs with no scrolling or overflow.
- Long titles are **truncated with an ellipsis** (`…`) at the tab's inner boundary.
- **Active tab** — uses the WinUI 3 `TabView` selected-state treatment; background
  matches the content area so tab and editor read as one continuous surface.
- **Inactive tabs** — slightly muted; no separator lines between tabs.

---

## 4. Default Tab Definitions

Applied on first launch, or whenever `settings.json` contains no `tabs` entry.

| # | Emoji | Title   |
|---|-------|---------|
| 1 | 📋    | Scratch |
| 2 | ✅    | Tasks   |
| 3 | 💡    | Ideas   |
| 4 | 🔗    | Links   |
| 5 | 📖    | Notes   |

---

## 5. Editing Emoji and Title

### Trigger

**Double-click** (or `F2` when the tab strip has keyboard focus) on a tab label opens an
inline edit popover anchored to that tab. A single click still switches to that tab
normally.

### Popover Layout

```
┌─────────────────────────────────┐
│  [emoji button]  [____________] │  ← emoji picker trigger + title field
│                  14 / 16 chars  │
│              [Cancel]  [Save]   │
└─────────────────────────────────┘
```

**Emoji button** — opens a dedicated emoji picker grid (see §5.1).

**Title field** — plain text input, maximum **16 characters**, enforced client-side with a
live character counter (`n / 16`). No error dialog is shown; input is blocked at the
limit.

**Save** (`Enter` or button) — persists both fields to `settings.json` and updates the
tab label in place with no flicker.

**Cancel** (`Esc` or button) — discards changes and closes the popover.

### Validation Rules

- Empty titles are allowed (saving an empty title is permitted and does not revert to the previous value).
- If the user closes the popover without touching the emoji field, the existing emoji is
  retained unchanged.

### 5.1 Emoji Picker

Use the **Emoji.WPF** or **EmojiPicker** NuGet package (evaluate at implementation time
for WinUI 3 compatibility) to render a self-contained scrollable grid of emoji inside the
popover. The Windows system picker (`Win + .`) is **not** used — the in-app grid keeps
the interaction contained to the panel.

Requirements for the picker component:

- Grouped by standard Unicode category (Smileys, Objects, Symbols, etc.)
- Search / filter field at the top
- Single-click on a glyph selects it, updates the emoji button preview, and closes the
  picker grid
- Recently used row (last 7 selections, persisted in `settings.json`)

---

## 6. Persistence

### Active Tab on Relaunch

The panel **always opens on Tab 1** on relaunch. Active tab state is not persisted.

### settings.json Schema

`settings.json` gains a `tabs` array. Buffer content files are unchanged.

```json
{
  "hotkey": "Ctrl+Shift+Space",
  "pinned": false,
  "recentEmoji": ["🔥", "⭐", "📌", "✏️", "🧠", "🗂️", "📎"],
  "tabs": [
    { "id": 1, "emoji": "📋", "title": "Scratch" },
    { "id": 2, "emoji": "✅", "title": "Tasks"   },
    { "id": 3, "emoji": "💡", "title": "Ideas"   },
    { "id": 4, "emoji": "🔗", "title": "Links"   },
    { "id": 5, "emoji": "📖", "title": "Notes"   }
  ]
}
```

Buffer content remains in `buffer_1.json` … `buffer_5.json`, keyed by `id`.
**Renaming a tab does not rename or migrate its file.**

---

## 7. Tray Tooltip

Hover text is updated to display each tab's current emoji and title alongside its first
line of content:

```
📋 Scratch  — Meeting agenda draft…
✅ Tasks    — [ ] Buy milk
💡 Ideas    — App idea: offline-first…
🔗 Links    — https://github.com/…
📖 Notes    — (empty)
```

---

## 8. Keyboard Navigation

| Key | Action |
|-----|--------|
| `Ctrl+1` … `Ctrl+5` | Switch to tab N (active while panel is open) |
| `F2` with a tab focused | Open edit popover for that tab |
| `Enter` in edit popover | Save and close popover |
| `Esc` | Close edit popover without saving |
