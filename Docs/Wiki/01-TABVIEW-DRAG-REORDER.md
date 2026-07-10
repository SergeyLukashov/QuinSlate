# TabView drag-reorder: the four SDK traps

> _Last updated: 2026-07-10_

How QuinSlate's five tabs came to be drag-reorderable, and the four separate WinUI `TabView`
behaviours that had to be worked around to make it look right. Every number below was measured
against the running packaged app (see [02-SYNTHETIC-INPUT-VERIFICATION.md](02-SYNTHETIC-INPUT-VERIFICATION.md)),
not inferred. The product-level rules live in [../Specs/14-TABS-REDESIGN.md](../Specs/14-TABS-REDESIGN.md).

---

## 0. Turning it on

```xml
<TabView CanReorderTabs="True" CanDragTabs="False" AllowDropTabs="True" ... />
```

The SDK template maps these straight onto the inner `ListView`:

| TabView property | ListView property | SDK default |
|---|---|---|
| `CanReorderTabs` | `CanReorderItems` | `true` |
| `AllowDropTabs`  | `AllowDrop`       | `true` |
| `CanDragTabs`    | `CanDragItems`    | **`false`** |

An in-list reorder needs `CanReorderItems` **and** `AllowDrop`. It does **not** need
`CanDragItems` — that governs dragging a tab *out* as a data payload, which QuinSlate has no
target for (the five buffers are fixed). So `CanDragTabs` stays off and no drag visual ever
leaves the window.

### Consequence: none of the drag events fire

Because `TabView` raises its drag events off `ListView.DragItemsStarting` / `DragItemsCompleted`,
and those are gated on `CanDragItems`, with `CanDragTabs="False"` **not one of these ever fires**
(confirmed by logging every handler):

`TabDragStarting`, `TabDragCompleted`, `TabStripDragOver`, `TabStripDrop`, `TabDroppedOutside`

**`TabItemsChanged` is the only signal a reorder happened.** Do not hang reorder logic off
`TabDragCompleted` — it looks correct and never runs.

`TabItemsChanged` also fires as the strip is first realized (one `Reset`, then one `ItemInserted`
per tab), and a single drag raises several `ItemRemoved`/`ItemInserted` pairs. So the handler
must: ignore any pass where `TabItems.Count` differs from the expected count (the dragged tab is
momentarily out of the collection), and only act when the resulting **order actually differs** from
the order already held. Without the second guard the strip's initial realization wrote
`settings.json` six times on every launch.

---

## 1. `TabWidthMode="Equal"` squeezes every tab for the duration of a drag

`TabView::UpdateTabWidths` in the Equal branch stamps an explicit `Width` on **every**
`TabViewItem`, and it re-runs from `OnItemsChanged` — which a reorder triggers. During the frame
where the dragged tab is out of the collection, the width it computes clamps to `minTabWidth`, so
all five tabs snapped from 122 DIP to exactly 100 DIP for the whole drag and sprang back on drop.

**Fix: `TabWidthMode="SizeToContent"`.** That branch leaves `Width` unset. The panel already pins
each tab's *header* width to the equal share (`TabStripCalculator.ComputeHeaderWidth`), which is
what made the tabs equal in the first place — Equal mode was contributing nothing here except this
bug. Mid-drag frames are now pixel-identical to rest.

---

## 2. A custom `TabViewItem` template must carry the `DragStates` group

The SDK's own `TabViewItem` template has a `DragStates` visual-state group. The reorder depends on
it: the items panel leaves the carried tab out of layout and re-arranges the rest around the drop
gap, and this group is what tells the carried container how to render. QuinSlate's template
(`Themes/BufferPanelResources.xaml`) defined only `CommonStates` and `DisabledStates`, so the
picked-up tab kept painting, opaque, at its pre-drag offset — colliding with whichever tab slid
into that slot.

Two traps inside the fix:

- **Do not "simplify" `Reordering` to `Opacity 0`.** The visual that follows the pointer is a
  *composition clone of that same element*, so zeroing the element blanks the clone too and the tab
  vanishes mid-drag. The SDK animates to `ListViewItemReorderThemeOpacity` (**0.8**), which is what
  makes the carried tab read as lifted rather than gone. (`ListViewItemDragThemeOpacity` is 0.8,
  `ListViewItemReorderTargetThemeOpacity` is 0.5.)
- **Declare every state name the SDK can request, even the empty ones.**
  `VisualStateManager.GoToState` with a name a group does not contain *fails and leaves the current
  state applied*. Omit, say, `ReorderedPlaceholder` and a tab is stranded at 0.8 opacity forever
  after a drop.

The group is mirrored verbatim from the SDK. `*Target` and `*Secondary` states apply to the tabs
being dragged *over* and must stay fully visible.

---

## 3. The strip's own chrome must be deducted, or the row drifts left

This one presented as: *drag a tab toward the end and the whole row ends up a few pixels left of
where it was; switching tabs fixes it.*

`ComputePerTabMaxWidth` divided up `tabViewWidth − header − footer`. But the strip consumes chrome
**inside** that region which it never subtracted:

- the strip `ScrollViewer`'s border takes **2 DIP** of viewport, and
- its `ItemsPresenter` pads the tab run by a further **8 DIP**.

Measured at rest on a 703 DIP window: `extent = 572.0` against `viewport = 561.2`. The five tabs
were always ~11 DIP wider than the strip could show, so **the strip was permanently scrollable**
even though the tabs looked like they fit. Nothing scrolls it in ordinary use — but a drag-reorder
does, and it stayed parked at that offset (`hoffset` went `0.00 → 10.80` and never came back),
drawing the whole row ~11 DIP left. It *appeared* to heal itself on a tab switch only because the
SDK scrolls the newly selected tab back into view, resetting the offset.

The same overshoot trips the SDK's own `SizeToContent` overflow test
(`itemsPresenter.ActualWidth > availableWidth`), surfacing the scroll chevrons on a strip whose
tabs plainly fit — measured at a 900 DIP window as `ip = 768` against an available `760`.

**Fix, in `TabStripCalculator`:** deduct a named `TabStripChrome` (12 DIP = the 10 measured plus
2 DIP of headroom, because layout snaps each tab's width up to a whole device pixel) and truncate
the per-tab share to a whole DIP. The tab run can then never exceed the viewport, so there is
nothing to scroll and nothing to overflow. A unit test asserts the invariant
(`tabRun + TabStripChrome <= available`) across a sweep of window widths.

### Why the offset is re-pinned from `ViewChanged`

`BufferPanel.ResetStripScrollWhenTabsFit` also forces the offset back to 0 whenever the tabs are
meant to fit. It is hooked to the strip `ScrollViewer`'s **`ViewChanged`**, not to the end of a
reorder: a one-shot reset driven off the collection change *does not work*, because the SDK scrolls
the dropped tab into view **after** the tab collection has settled and overwrites it. Below the
per-tab floor the strip genuinely scrolls and the offset is the user's to keep.

This is now redundant with the sizing fix above, and is kept as cheap insurance against residual
rounding.

---

## 4. Order is persisted by array order, and identity lives in `Tag`

Enabling reorder without this would look like it worked and then silently lose the order.

- **`settings.json`'s `Tabs` array order *is* the left-to-right tab order.** `GetTabs()` used to
  rebuild the list by walking the defaults 1→5 and looking each id up, discarding the persisted
  sequence on every read; `BufferPanel.Initialise` built tabs in buffer order regardless. Both now
  honour the array order. `GetTabs()` drops unknown/duplicate ids and appends any missing id in
  default order, so the result always holds all five exactly once.
- **A tab's buffer identity travels with the tab, in `TabViewItem.Tag`.** Reordering never moves
  text between buffers and never renames a buffer file. Anything that resolves "which buffer is
  this tab" must read `Tag` — `F2` (`BufferKeyboardController.RequestEditFlyout`) used
  `SelectedIndex + 1`, which renames the wrong buffer the moment tabs move.
- **`Ctrl+1`…`Ctrl+5` select by *position*,** so the shortcut follows a reorder. The tray peek rows
  therefore follow the tab order too, numbered by position rather than by buffer id
  (see [../Specs/07-BUFFER-PEEK.md](../Specs/07-BUFFER-PEEK.md)).
- **The editor must ignore the transient mid-reorder selection.** Pulling the dragged tab out of
  `TabItems` moves the selection onto whichever tab shifted into its slot; activating that buffer
  would swap the editor's text — and replay its entrance animation — under the user's pointer, then
  swap back on drop. `SyncActiveBuffer` skips any pass where the tab count is short, and treats a
  re-selection of the same buffer as a no-op.

---

## Known gaps

- **Dragging a tab that is not the active one does not activate it.** A plain click selects
  normally; only the drag path differs (likely the `GettingFocus` cancel in
  `BufferPanel.OnTabItemGettingFocus`). Browsers select the tab the moment you pick it up.
- The tabs now sit up to ~5 DIP narrower in total than the strip. Invisible, but real.
- Behaviour on a **maximized** window has not been verified; the sweep covered 380–900 DIP.
