# Docs conventions

> _Last updated: 2026-07-14_

Everything under `Docs/` (`Specs/`, `Investigations/`, `Decisions/`, `Plans/`, `Wiki/`) follows
one naming and stamping convention. Apply it to every new or renamed doc.

- **Filename:** `NN-KEBAB-NAME.md` — a two-digit ordinal prefix, then an UPPERCASE
  kebab-case name, `.md`. No type token in the name (the folder already conveys the type:
  spec / investigation / decision / plan / wiki). The ordinal is per-folder and orders the files;
  `00-` is reserved for a folder's index/queue (e.g. `Specs/00-FEATURE-QUEUE.md`,
  `Wiki/00-INDEX.md`). Pick the next free number in that folder. Investigations and plans are
  numbered chronologically.
- **Date stamp:** every doc opens with its H1 title immediately followed by a
  `> _Last updated: YYYY-MM-DD_` blockquote. Refresh that date (ISO `YYYY-MM-DD`) whenever
  you edit the doc.
- **Cross-links:** reference other docs by their current filename. When you rename a doc,
  update every link to it (other docs, `CLAUDE.md`, and code comments).

Folder purposes (also summarized in [00-INDEX.md](00-INDEX.md)):

- **`Specs/`** — what the product does.
- **`Decisions/`** — a choice made, its alternatives, and why (ADRs).
- **`Investigations/`** — the record of one bug hunt, symptom to fix.
- **`Plans/`** — implementation plans for multi-step work.
- **`Wiki/`** — the distilled residue of the above: "read this before you change X".
