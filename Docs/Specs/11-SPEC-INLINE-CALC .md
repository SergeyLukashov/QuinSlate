# SPEC: Inline calculator

## What
When a line ends with `=` (with or without a preceding space) and the user
types the `=` character, QuinSlate evaluates the mathematical expression to the
left of the `=` and appends the result on the same line — instantly,
without pressing Enter.

    subtotal * 1.21 =         →   subtotal * 1.21 = 594.77
    (450 + 38.50) / 4 =       →   (450 + 38.50) / 4 = 122.13
    2^10 =                    →   2^10 = 1024
    1+2=                      →   1+2=3

---

## Trigger

Evaluation fires the moment the user types `=`, if all of the following
are true:

1. The character just typed is `=`.
2. The line up to the caret ends with `=` (optionally preceded by a space).
3. The adjacent-operator guard passes (see below).
4. The expression heuristic passes (see below).

If any condition fails, the `=` is inserted normally with no side effects.

### Adjacent-operator guard

Check the character immediately before the `=` (ignoring the optional
preceding space). If it is one of `> < ! =`, do not evaluate. This
prevents `>=`, `<=`, `!=`, `==` from triggering in both the spaced and
no-space forms.

    450 + 38 =     → triggers   (digit before the =)
    1+2=           → triggers   (digit before the =, no space)
    a >= b =       → no trigger (> immediately before the space-=)
    a >= b=        → no trigger (> immediately before =)
    x == y=        → no trigger (= immediately before =)

---

## Expression heuristic

Confirm the left-hand side looks like math. Reject silently unless the
LHS contains:

- At least one **digit** (`0–9`), AND
- At least one **operator** from the set `+ - * / % ^ ( )`

This prevents firing on prose like `status =` or `name =`.

---

## Behaviour

### Happy path

1. User types `(450 + 38.50) * 1.21 =`.
2. On the `=` keypress, QuinSlate evaluates the expression to the left.
3. The line is rewritten in place to:

       (450 + 38.50) * 1.21 = 594.785

4. Cursor is placed at the end of the line, after the result.
5. Buffer is marked dirty; the 300 ms debounce write starts.

### Result formatting

- Whole number result: no decimal point. `2^10 = 1024`, not `1024.0`.
- Fractional result: round to 6 significant digits, strip trailing zeros.
  `1 / 3 = 0.333333`. `1.10 * 2 = 2.2`.
- No currency symbols, thousand separators, or units.

### Parse failure

If the expression fails to parse or evaluate (bad syntax, unknown token,
division by zero, overflow), insert the `=` character normally. No error
dialog, no tray notification — the user sees the `=` appear and can
continue editing.

Division by zero: silent fallback, not `Infinity` or `NaN`.

### No variable support

The calculator evaluates the literal expression on the line only. There
is no symbol table. `rate * hours =` where `rate` is a plain word will
fail the heuristic or fail to parse and fall through silently. Variable
support is deferred to a future tier.

---

## Library

Use **NCalc** from `ncalc/ncalc` — the active successor to the archived
NCalc2. NuGet package: `NCalcSync`.

Do not use **NCalc2** (archived August 2025, read-only). Do not use
**mXparser** (dual license — commercial use requires a paid licence,
which is a blocker for distributing a free app).

NCalc handles operator precedence, parentheses, modulo (`%`), and
`System.Math` functions without custom configuration.

**NCalcSync 5.x deviation:** `^` is parsed as bitwise XOR in NCalcSync 5.x,
not exponentiation. `CalcService` pre-processes expressions before evaluation:
each top-level `^` is rewritten as `Pow(left, right)` to restore the
expected exponentiation behaviour.

---

## Control: RichEditBox, not TextBox

Use `RichEditBox` for all buffer text areas.

Reason: `TextBox` loses its undo history when `.Text` is assigned
programmatically. `RichEditBox` exposes `ITextDocument`, which supports
range-based replacement via `GetRange()` → `SetText()`. This preserves
the undo stack — Ctrl+Z after an evaluation correctly reverts to the
un-evaluated line.

---

## Implementation

### Hook point

Attach to `CharacterReceived` on the `RichEditBox` to detect the `=` keystroke.
`CharacterReceived` delivers the composed character for the active keyboard
layout, so the trigger fires for `=` no matter where it sits on the physical
keyboard or whether it needs Shift/AltGr (a US-layout `VK_OEM_PLUS` virtual-key
check does **not** work for non-US layouts such as Spanish, German, or French).
The event fires after `KeyDown` and before the asynchronous `TextChanged`, so
record that `=` was typed, then on the subsequent `TextChanged` read the current
line via `ITextDocument`, apply the adjacent guard and heuristic, and either
rewrite the line in place or leave the inserted `=` untouched.

### Line rewrite

Use `ITextDocument.GetRange()` expanded to the current line, then
`SetText()` on that range to replace the line content with the evaluated
result. Do not assign to the control's `Text` property — that nukes
the undo stack.

Place the cursor at the end of the rewritten line after replacement.

### CalcService

Put the guard, heuristic, and evaluate logic in `Services/CalcService.cs`
as `internal static` methods. Keep all evaluation logic out of the view
code-behind.

### Undo

Range-based `SetText()` preserves the undo stack on `RichEditBox`. If
testing reveals it does not on the shipping WinUI 3 version, bracket the
replacement with `BeginUndoGroup()` / `EndUndoGroup()` — verify before
shipping.

---

## Edge cases

- **Leading minus** — `-5 * 3 =` must evaluate to `-15`. NCalc handles
  unary minus natively; no special handling needed.

- **Whitespace inside expression** — `4 * ( 2 + 1 ) =` is valid.
  NCalc is whitespace-tolerant.

- **Double `=` typed** — the adjacent guard catches `=` as the last
  character before the space and rejects evaluation. No double-trigger.

- **Caret mid-line** — only evaluate if the caret is at or after the last
  non-whitespace character of the line. If the user is editing inside
  existing text, insert the `=` normally.

- **Locale decimal separator** — NCalc uses `.` regardless of system
  locale. Do not normalise `,` to `.` in v1. Known limitation.

- **Overflow** — results exceeding `double` range produce `Infinity`;
  treat as parse failure and fall through silently.

---

## Out of scope for v1

- Variable assignment and symbol table (`a = 5`, reused below)
- Live ghost-text preview while typing
- Unit conversion (`5 kg in lb =`)
- Multi-line expressions
- Result formatting options (currency, decimal places)
- Configurable trigger character
