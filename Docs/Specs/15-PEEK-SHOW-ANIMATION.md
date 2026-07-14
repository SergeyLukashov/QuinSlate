# 15 — SPEC: Tray Peek Show Animation

> _Last updated: 2026-07-14_

> **Status: Implemented.** The entrance animation is a hybrid transition combining a
> stationary window-level fade-in (via `SetLayeredWindowAttributes` over a timer) with a
> content-level slide-up/down. This fades the entire window smoothly into view without
> janky window position updates.
>
> **Update 2026-07-07 — `WS_EX_LAYERED` is permanent, not per-show.** The style used to
> be added before each show and stripped when the fade finished. Toggling the layered
> bit while the XAML island is mid-composition intermittently fail-fasted the process
> inside `Microsoft.UI.Xaml.dll` (stowed exception `0xc000027b` / `E_UNEXPECTED`) on
> slower GPUs — see
> [04-TRAY-PEEK-HOVER-FAILFAST-CRASH.md](../Investigations/04-TRAY-PEEK-HOVER-FAILFAST-CRASH.md).
> The window is now layered from creation with alpha resting at 0; the fade ramp is
> unchanged. Do not reintroduce the style toggle.
>
> **Update — backdrop is now the dithered gradient, not Mica.** The whole app (including
> the peek window) later dropped Mica/Acrylic system backdrops for the opaque, in-tree
> `DitheredGradientBrushFactory` mesh (`TrayPeekPanel` paints `RootBorder` with it; see
> [05-BACKGROUND-GRADIENT-DITHERING.md](../Wiki/05-BACKGROUND-GRADIENT-DITHERING.md)). The Mica investigation below is retained as
> historical dead-end archaeology, but its central obstacle no longer applies: the gradient
> is an ordinary in-tree `ImageBrush`, so a content composition animation moves it together
> with the content — unlike external Mica, which DWM renders below the compositor. The
> project has also since moved to **Windows App SDK 2.2.0** (the notes below were written
> against 2.0.1), so the SDK-version-specific API gaps may no longer hold.

## Goal

When the tray peek window appears (on tray-icon hover, see
[07-BUFFER-PEEK.md](07-BUFFER-PEEK.md)), it should play a polished
entrance animation: the **whole window** should **fade in and slide up from the
bottom**, similar to:

- the app's own emoji picker entrance (`EntranceThemeTransition { FromVerticalOffset = 8 }` on the flyout presenter — see `QuinSlate.Ui/Components/EmojiPicker.cs`), and
- Windows 11's own taskbar app-preview flyouts, which animate smoothly.

The hard requirement that broke every attempt: the **background/frame must
animate too**, not just the text content inside the window.

## Relevant code

- `QuinSlate.Ui/Tray/TrayPeekWindow.cs` — borderless, **non-activating tool window**
  (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `HWND_TOPMOST`, subclassed `WndProc`
  returns `MA_NOACTIVATE`). Uses a manually-managed `MicaController`
  (`SetupActiveMicaBackdrop`) with `IsInputActive = true` so Mica stays "active"
  on a window that never gets focus. Shown via `appWindow.MoveAndResize(...)` +
  `ShowWindow(SW_SHOWNOACTIVATE)`.
- `QuinSlate.Ui/Tray/TrayPeekPanel.xaml(.cs)` — the content; root is
  `<Border x:Name="RootBorder" CornerRadius="12">`.
- `QuinSlate.Ui/Interop/NativeMethods.cs` — all P/Invoke.

## Attempts (chronological) and why each failed

### 1. Content-only Composition animation
Animated `RootBorder`'s composition visual (Opacity 0→1, Translation.Y small→0)
via `ElementCompositionPreview.GetElementVisual`, re-triggered on every show.

**Result:** smooth, but **only the content moved** — the Mica frame/background
stayed put. Rejected: the window itself did not appear to animate.

### 2. Whole-window slide via `SetWindowPos` on a `DispatcherTimer`
Showed the window ~24px below its final Y, then moved the real HWND up to final
Y over ~180ms with an eased `DispatcherTimer` (~15ms) loop.

**Result:** the whole window (Mica included) moved, **but it was laggy/janky.**

### 3. Same slide, optimized for smoothness
Replaced the `DispatcherTimer` with the vsync-aligned
`Microsoft.UI.Xaml.Media.CompositionTarget.Rendering` event, and stopped
re-asserting `HWND_TOPMOST` every frame (used `SWP_NOZORDER | SWP_NOSIZE |
SWP_NOACTIVATE`, `hWndInsertAfter = IntPtr.Zero`) to avoid per-frame DWM z-order
recompute.

**Result:** better, **but still laggy.** This is a hard ceiling (see findings).

### 4. Composition-backdrop rework (drop system Mica, draw backdrop in-tree)
Removed `MicaController`; attempted to draw the panel background ourselves with a
`CompositionBackdropBrush` on an in-tree `SpriteVisual`, so a single render-thread
animation of the content-root visual would move background + content together.
Window shown once at final position (no HWND move). DWM corner rounding via
`DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)`.

**Result:** the backdrop-brush plan could not produce a real Mica/wallpaper
effect (see findings), so it degraded to a **flat solid tint** background.
Critically, per testing this **still did not animate the whole window** — the
observed effect was again essentially content-only, while the Mica material was
lost. So even this complex approach failed the goal. **Reverted.**

## Findings (the important part for next time)

1. **System backdrops are "external content" the WinUI compositor cannot
   animate.** Per MS docs, `MicaController`/`MicaBackdrop`/`DesktopAcrylicController`
   are rendered by DWM *below* the compositor's surface with a hole punched out;
   the compositor can't see or transform those pixels. This is *why* a content
   composition animation never moves Mica.
   - https://learn.microsoft.com/windows/apps/develop/composition/visual-layer#differences-from-uwp

2. **The only way to move external Mica is to move the HWND** (`SetWindowPos`),
   which is inherently janky: DWM must recomposite the backdrop on every move,
   off the GPU fast-path. Vsync alignment + no z-order churn helps but does not
   make it smooth. There is a hard ceiling here.

3. **`Compositor.TryCreateBlurredWallpaperBackdropBrush()` is UWP-only.** It
   exists on `Windows.UI.Composition.Compositor` but **NOT** on the WASDK
   `Microsoft.UI.Composition.Compositor` in **Windows App SDK 2.0.1**
   (build-probed: `CS1061`; absent from `Microsoft.UI.winmd`). So the
   "blurred wallpaper = cheap Mica base" trick is unavailable.

4. **`Compositor.CreateBackdropBrush()` samples the app's own content, not the
   desktop.** Inside an opaque WinUI 3 desktop window it blurs transparent-black
   (empty) pixels — useless as a window backdrop — because **WinUI 3 desktop
   windows have no supported per-pixel transparency**. You cannot see the
   wallpaper behind the window through a composition brush.

5. **`ContentExternalBackdropLink`** (`Microsoft.UI.Content`) is the one API that
   can host a real system backdrop (Mica/Acrylic) on an arbitrary, transformable
   composition visual — i.e. it *could* give real Mica that animates. **But it is
   marked `[Experimental]` in WASDK 2.0 and is not in the stable 2.0.1 package.**
   Using it requires the experimental SDK channel.
   - https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.content.contentexternalbackdroplink

### Net conclusion
On **stable Windows App SDK 2.0.1**, "real Mica material" and "smooth whole-window
entrance motion" are effectively **mutually exclusive**. None of the attempts
delivered a smooth animation of the whole window *with* Mica.

## Open question for the next attempt
Attempt 4 dropped Mica yet *still* only appeared to animate the content, not the
whole window. Before trying anything else, determine **why**: e.g. was the
backdrop `SpriteVisual` actually parented under the animated `RootBorder` visual
(so it inherits the Translation/Opacity), or was it attached via
`SetElementChildVisual` on a sibling/host that the animation didn't touch? Does
the DWM-rounded window frame visibly fail to move because the HWND is static?
Pinning this down is the key to any future approach.

## Options to evaluate next time
1. **Upgrade Windows App SDK** to a version where `ContentExternalBackdropLink`
   is **stable**, then host real Mica on a composition visual and animate that
   visual on the render thread (the "proper" path). Verify smoothness + tone
   match against the main window's Mica.
2. **Accept content-only animation** (Attempt 1): smooth, keeps real Mica, but
   the frame doesn't slide. Simplest; previously rejected, may be acceptable if
   the alternative is no animation.
3. **Custom-drawn layered window** (e.g. `UpdateLayeredWindow` / DirectComposition
   surface you fully own, with a hand-built Mica-like material): full control,
   high complexity and tone-match risk.
4. Investigate community backdrops (e.g. WinUIEx custom backdrops) — but note the
   project rule against new NuGet packages unless absolutely necessary.

## Constraints any implementation must respect
- Keep the non-activating tool-window behavior (`WS_EX_NOACTIVATE`,
  `WS_EX_TOOLWINDOW`, `HWND_TOPMOST`, `WM_MOUSEACTIVATE`→`MA_NOACTIVATE`) and the
  hover-tracking + show-delay timers intact.
- All P/Invoke in `NativeMethods.cs`. No nullable annotations. No magic numbers.
  No new NuGet packages unless absolutely necessary. UI work goes through the
  WinUI 3 Expert agent.
- Mica tone must match the main window (the team is particular about this — see
  the manually-managed `MicaController` theme-resolution logic in
  `SetupActiveMicaBackdrop`/`ApplyResolvedBackdropTheme`).
