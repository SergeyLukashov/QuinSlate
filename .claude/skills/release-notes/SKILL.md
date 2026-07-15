---
name: release-notes
description: Turn the commits since the last version tag (v*.*.*) into release notes for the next QuinSlate release. Produces two things - a short, plain-language "What's New" list for the Microsoft Store listing or a GitHub release, and a longer technical changelog written to Docs/ReleaseNotes/ for the team. Use this whenever the user asks for release notes, a changelog, a "what's new" section, "what changed since the last release/tag/version", or is about to cut a release and needs something to show users. Also use it when they just say "summarize the commits since v0.9.6".
---

# Release Notes

One pass over the commits, two audiences, two very different documents:

| | Audience | Voice | Length |
|---|---|---|---|
| **What's New** (chat, for the Store / GitHub release) | someone who has never opened the repo | plain language, zero jargon | 3-6 bullets |
| **`Docs/ReleaseNotes/vX.Y.Z.md`** (internal) | you, six months from now, wondering what shipped | technical, specific, names the code | same bullets, one notch longer |

The user-facing list does not care that the editor is now CodeMirror in a WebView2,
only that typing feels better. The internal doc cares about both, and says which
commit did it.

Produce **both, every time**. The `Docs/` file is not optional and not a
"here's what I would have written" - write it with the Write tool before you finish.

**Two rules that hold in both documents, no exceptions: no em dashes, and no emoji.**
Em dashes are the single most reliable tell that a text was machine-written, so
restructure the sentence, use a comma, or split it in two. Check your output for them
before you finish; they creep in on the last bullet when you stop watching.

## The process

**1. Find the range.**

```bash
git describe --tags --abbrev=0 --match "v[0-9]*.[0-9]*.[0-9]*"
git log --no-merges --pretty=format:'%h%x09%s%x09%b' <last-tag>..HEAD
```

If there is no matching tag, use the whole history (`git log --no-merges`) and note
that this is the first release.

**2. Read past the subject lines.** Commit subjects are written for other developers
and routinely bury the thing users will actually notice. Read the bodies. When a
subject is vague, opaque, or sounds internal but might not be, look at what it
touched (`git show --stat <sha>`, and the diff if you still can't tell). A commit
named "Restructure startup timeline" sounds like plumbing; the diff shows the window
no longer flashes a half-drawn panel when it opens. That's a headline.

This reading is the expensive part, and it feeds both documents. Do it once,
properly, and take notes as you go: for each commit, what changed in the code, and
what (if anything) a user would notice. The two write-ups are then just two views of
those notes.

**3. Sort each commit into: user-visible, internal, or invisible.**

- **User-visible** changes earn a bullet in *both* documents.
- **Internal** changes (refactors, architecture, perf work with no felt effect,
  new tests, dependency swaps) appear in the `Docs/` file only.
- **Invisible** noise (typo fixes, README badge links, formatting) appears in
  neither. Don't pad either document with it.

The user-facing test is not "was this a big change" but "would a user notice if I
didn't tell them?" Size and visibility come apart in both directions:

- A ground-up rewrite billed as like-for-like earns **no** user-facing bullet for the
  rewrite itself. Nobody browsing the Store cares which framework the editor is built
  on. (It's a headline in the internal doc, obviously.)
- But large commits smuggle user-facing wins in with the plumbing. That same rewrite
  quietly raised the buffer size cap and fixed how the caret steps over emoji, neither
  of which is mentioned in the subject line. **Those** are the user-facing bullets.
  Mine the big commits; don't take "like-for-like" at its word.

**4. Group by what changed, not by commit.** Three commits fixing one flicker are one
bullet. One commit that both adds a feature and fixes an unrelated bug is two. There
is no commit-to-bullet correspondence in either document.

**5. Get the version number.** The upcoming version is the `Identity Version` in
[QuinSlate.Ui/Package.appxmanifest](QuinSlate.Ui/Package.appxmanifest), minus the
trailing revision field: `0.9.7.0` in the manifest means this release is `0.9.7`. If
it still equals the last tag, the version hasn't been bumped yet. Use it anyway and
say so.

## Document A: "What's New" (chat)

Print it in a fenced block so it's cleanly copyable into the Store listing or a
GitHub release. Just the bullets, no title line and no version number inside the
block, since the Store listing and the GitHub release UI both already show the
version elsewhere and a hardcoded title is one more thing to edit out before pasting:

```
- QuinSlate opens clean, with no flicker or half-drawn panel on the way in
- Long notes render to the last line, and a buffer now holds up to a million characters
- Drag tabs to put your buffers in the order you actually think in
- Emoji show in full colour again in tab names, the picker, and your notes
```

**3-6 bullets**, one line each, roughly 8-15 words. More than eight means you are
listing commits instead of listing changes.

**Sort by significance, most significant first.** These are fragments, not
sentences, so nothing else signals importance the way a headline font would in
print - order is the only signal you get. Ask "which of these would the user be
happiest to read first" and put that one on top; small polish and cosmetic fixes
sink to the bottom. This is independent of commit order or how big the diff was, a
one-line fix for silently-dropped keystrokes outranks a large but low-stakes visual
tweak.

**No trailing period on these bullets.** They read as short, punchy fragments (the
way Store listings and GitHub release notes format them), and a trailing period on
a fragment reads as a typo, not as polish. This rule is specific to this document -
Document B's bullets are full technical sentences and keep normal punctuation.

Write what changed and what they can now do, in plain words, present tense. Name the
actual thing ("tabs", "emoji picker", "startup"); vague nouns like "the experience"
or "performance" are how notes turn to mush.

**Examples**

Commit: `Add drag-to-reorder for buffer tabs`
Bullet: `Drag tabs to put your buffers in the order you actually think in`

Commit: `Fix monochrome emoji glyph in tab headers and picker button`
Bullet: `Emoji show in full colour again in tab names and the picker`

Commit: `Update Microsoft Store badge link and target`
Bullet: *(none, nothing changed in the app)*

### Staying out of slop territory

The failure mode here isn't dryness, it's the LLM press-release voice: cheerful,
weightless, and identical to every other release note ever generated. Tells to catch
yourself on:

- Openers that announce rather than inform: "We're excited to announce", "Say goodbye
  to", "Introducing".
- Compliments the software pays itself: "seamless", "powerful", "blazing fast",
  "delightful", "robust", "significantly enhanced".
- Filler bullets that assert quality without content: "Various improvements and bug
  fixes", "Enhanced performance and stability".
- Every line ending in an exclamation mark.

The cure is specifics. "Faster startup!" is slop; "QuinSlate opens clean, with no
flicker on the way in" is the same fact with something in it. If a bullet would
survive being pasted into a *different* app's release notes, it's too generic.
Rewrite it until it could only be about this change.

Be warm and human, but let the changes carry it. A little dry wit is welcome. Hype is
not.

**Never write a bullet you haven't verified in the diff.** Inventing a plausible
feature that shipped in no commit is the one unrecoverable error here: users try it,
it isn't there. If a change is ambiguous and you can't tell what the user sees, ask
rather than guess.

## Document B: `Docs/ReleaseNotes/vX.Y.Z.md` (internal)

**Same bullet list, one notch longer, with the technical content put back.** This is a
changelog, not an architecture essay. If Document A is one line per change, this is
one bullet plus a sub-bullet or two per change: what changed in the code, and why if
the why isn't obvious from the what.

Name things precisely, because that's the whole point of this file existing: the
classes, the files, the constants, the commit hash. Cross-link the ADR or spec if one
covers the change rather than restating it. A future reader should be able to find the
code from the bullet.

Keep it proportional: a 5-commit release is a page at most. If you're writing
paragraphs, or listing every deleted class, you've drifted into rewriting the ADR.
Trust the cross-link and move on.

Create `Docs/ReleaseNotes/` if it doesn't exist. Files are named by version rather
than the `NN-KEBAB-NAME.md` convention used elsewhere in `Docs/`, because here the
version *is* the ordinal. Keep the repo's date-stamp convention.

```markdown
# QuinSlate 0.9.7
> _Last updated: 2026-07-13_

Released from `v0.9.6..72c9a1a` (5 commits).

## User-facing

- **Editor rebuilt on CodeMirror 6** (`344638c`). RichEditBox replaced by a single
  WebView2 hosting a vendored CM6 bundle, driven to escape the RichEditBox render
  ceiling that made the tail of long buffers invisible. See ADR
  `Docs/Decisions/04-EDITOR-CODEMIRROR-WEBVIEW2.md`. Scoped like-for-like, but lifted
  four limits:
  - `AppConstants.MaxBufferLength` raised from `50_000` to `1_000_000`; the old cap
    only existed to stay under the render ceiling.
  - Colour emoji render in the buffer body (RichEditBox forced the monochrome glyph).
  - Caret and Backspace operate on grapheme clusters, so a ZWJ emoji is one character.
  - Calc-result highlight fade is now a true alpha fade.
- **Tabs drag to reorder** (`6cea22e`), persisted to `settings.json`.
- **Startup no longer pops in** (`72c9a1a`). Window stays cloaked until the first
  frame is composited; see `Docs/Plans/…`.

## Internal

- `ccee251` README Store badge link and target. No app code.
```

The two sections are a default, not a template to fill mechanically. If a release is
four small fixes with nothing internal, a flat list is the right shape.

## Finishing up

After both are written, tell the user in one line which commit range you covered and
how many commits you dropped as not user-facing, so they can sanity-check that nothing
visible got lost.

Then stop. Committing the notes, bumping the version, and cutting the tag are the
user's calls. Don't do them unasked.

## Edge cases

- **Nothing user-facing in the range.** Don't manufacture bullets. Say the release is
  internal-only, still write the `Docs/` file, and ask whether they want a Store
  "What's New" at all.
- **Uncommitted work in the tree.** Out of scope. The notes describe committed history;
  an unfinished feature in the working tree hasn't shipped. (The version in the
  manifest is the exception: read it even if the bump isn't committed yet.)
- **A commit reverts an earlier one in the same range.** Neither gets a user-facing
  bullet, since the user never saw either. Worth a line in the internal doc.
- **A fix for a bug introduced *since* the last tag.** No user-facing bullet: users
  never had the broken version, so "fixed X" announces a bug they never met. It still
  belongs in the internal doc.