# SPEC: Manual theme switch

> _Last updated: 2026-07-19_

## What
A user-selectable app theme: **System default**, **Light**, or **Dark**. The default is
**System default**, which follows the current Windows app theme and keeps tracking it live when the
user changes Windows between light and dark. Choosing Light or Dark pins the app to that theme
regardless of the Windows setting. The choice persists across restarts.

## UI
A **Theme** submenu in the tray context menu (see [03-TRAY-MENU.md](03-TRAY-MENU.md)), grouped with
the other persistent settings:

    Theme ▸   System default  (•)
              Light
              Dark

The three entries are mutually exclusive radio items; the one in effect is checked. Selecting one
applies it immediately and closes the menu. The submenu was chosen over a two-state toggle because a
toggle cannot express "follow Windows", which is the default the product wants to preserve, and over
an in-panel control to keep the minimal editor chrome uncluttered and the setting reachable while the
panel is hidden.

## Behaviour
- Persisted in `settings.json` as `Theme` (`"System"` | `"Light"` | `"Dark"`; default `"System"`).
- A missing or unrecognised value is treated as `System`.
- The change is applied without a restart and takes effect on **every** surface: the main panel and
  its CodeMirror editor, the About card, the tray peek preview, and the tray menu (and its Theme
  cascade) — including each window's DWM frame border.

## Implementation

### Single source of truth
`Services/ThemeService.cs` owns the mapping and holds the persisted preference (via `SettingsService`).
WinUI 3 cannot change `Application.RequestedTheme` after startup, so the switch is applied per window
by setting `FrameworkElement.RequestedTheme` on each window's **content root**:

- `System` maps to `ElementTheme.Default` (follows the Windows app theme live, raising
  `ActualThemeChanged` when Windows flips).
- `Light`/`Dark` map to the matching `ElementTheme`.

Every window derives its theme from its own content root — seeded from `ThemeService` at construction
(`MainWindow` registers its root **before** the panel builds its first mesh, so the first paint
already matches; the tray peek, About card, and tray menu each adopt the current theme when created).
Each component then reacts to its **own** `ActualThemeChanged`, so a preference change *or* a live
Windows light/dark flip is handled the same way.

Long-lived windows that can be open while the theme is switched **register** with `ThemeService`, so
`Set` pushes the new `RequestedTheme` straight to them: `MainWindow` (always) and the **About card**
(the tray icon — and thus the Theme submenu — stays reachable even behind the About modal, so the
card must re-theme live). The tray peek receives the same push via `TrayPeekWindow.SetRequestedTheme`.
Registered roots are unregistered when their window closes.

Reused popup content is **outside** the themed visual tree between opens (the tab rename flyout builds
its view once and caches it). Setting `RequestedTheme` on such a cached view at open time is *not*
enough: a view already realized in the previous theme keeps that theme's brushes for the first frame
when reshown (the visible "Cancel button flashes the old colour" symptom). Instead the rename flyout
records the theme it was built for and **rebuilds the view when the theme differs** at open, so its
very first realization paints in the current theme. The rebuild only happens on an actual theme
change, so the common case still reuses the cached view.

### The two rules that make every surface follow
1. **Never read a theme-varying brush from `Application.Current.Resources`** — that resolves against
   the app/Windows theme, so a pinned override that differs from Windows freezes the wrong colour.
   All themed brushes come from XAML `{ThemeResource}` (which resolves against the element's theme and
   updates live): the tray peek's row text, and the tray menu's presenter background
   (`TrayMenuFlyoutPresenterStyle` in `App.xaml`). The gradient mesh is exempt — it reads the explicit
   `AppGradient*Dark/Light` colour keys by name, which are theme-independent by construction.
2. **Update the non-XAML surfaces by hand on `ActualThemeChanged`** — anything outside the WinUI
   compositor does not follow `{ThemeResource}`:
   - The **DWM frame border** follows the *Windows* theme and ignores a per-element override, so each
     window sets `DWMWA_USE_IMMERSIVE_DARK_MODE` (`NativeMethods.SetImmersiveDarkMode`) from its
     resolved theme, initially and on every change. Without this a pinned-dark window shows a light
     hairline border (this was the visible "About doesn't switch" symptom).
   - The **Win32 erase fill** (`WindowBackgroundBrush`, the pre-compositor flash guard) is re-themed
     on `ActualThemeChanged`.
   - The **CodeMirror editor** colours are re-pushed over the bridge from the panel's
     `ActualThemeChanged` (previously only `UISettings.ColorValuesChanged` did this, which does not
     fire for a manual override that leaves the Windows theme unchanged).

MainWindow hides the system title bar (`SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false)`),
so no caption-button theming is required — a per-element override does not affect system caption
buttons, but there are none here.

## Not in scope
- A high-contrast-specific palette beyond what WinUI resolves automatically.
- Per-buffer or scheduled (time-of-day) theming.
