// Calc result highlight (mark decoration that fades over 1600 ms via CSS).

import { StateField, StateEffect } from "@codemirror/state";
import { EditorView, Decoration } from "@codemirror/view";

const CALC_FADE_MS = 1600; // Docs/Specs/12-CALC-RESULT-ANIMATION.md

const setCalcHighlight = StateEffect.define();
const clearCalcHighlight = StateEffect.define();

export const calcHighlightField = StateField.define({
  create() {
    return Decoration.none;
  },
  update(deco, tr) {
    deco = deco.map(tr.changes);
    for (const effect of tr.effects) {
      if (effect.is(setCalcHighlight)) {
        const { from, to, accent } = effect.value;
        deco = Decoration.set([
          Decoration.mark({
            class: "cm-calc-highlight",
            attributes: { style: `--calc-accent:${accent}` },
          }).range(from, to),
        ]);
        return deco;
      }
      if (effect.is(clearCalcHighlight)) {
        return Decoration.none;
      }
    }
    // Any user edit while a highlight is showing cancels it instantly (the calc
    // rewrite itself carries setCalcHighlight above and is handled before this).
    if (tr.docChanged && deco.size > 0) {
      return Decoration.none;
    }
    return deco;
  },
  provide: (field) => EditorView.decorations.from(field),
});

let calcClearTimer = null;

export function startCalcHighlight(view, from, to, accent) {
  if (calcClearTimer != null) {
    clearTimeout(calcClearTimer);
    calcClearTimer = null;
  }
  view.dispatch({ effects: setCalcHighlight.of({ from, to, accent }) });
  calcClearTimer = setTimeout(() => {
    calcClearTimer = null;
    view.dispatch({ effects: clearCalcHighlight.of(null) });
  }, CALC_FADE_MS);
}
