# SPEC: Dictate into slot

## What

The user presses a button in the panel — or a hotkey while the panel is
focused — speaks, and the transcribed text is appended to the active
buffer. The panel must be open. No cloud, no API key, no installed
dependencies.

---

## Motivation

Keyboard capture covers the hands-on-keyboard case. Capture without
opening (feature 10) covers the mouse-selection case. Dictation covers
the hands-full or think-faster-than-you-type case. All three are the
same action — get a thought into a buffer — via different input modalities.

---

## Speech engine

**v1: `Windows.Media.SpeechRecognition`** — the modern Windows inbox
engine. Available on all supported hardware. No download, no extra
permission beyond the microphone capability, works offline. Accuracy is
adequate for short-burst scratchpad capture; it is not a transcription
tool.

**Not in v1: Whisper.net** — substantially better accuracy but requires
Windows 11 or newer. Revisit when Windows 10 share falls below ~15%
(projected late 2027). The upgrade path is a backend swap behind the same
UI contract; the rest of this spec applies to both.

---

## Microphone permission

The app manifest must declare the `microphone` capability. On first use
of dictation, Windows shows a one-time system consent dialog. QuinSlate has
no control over this dialog. After the user grants permission, it does
not appear again.

If permission is denied, the mic button is disabled and a tooltip reads
"Microphone access was denied. Enable it in Windows Privacy Settings."
No other error UI is needed.

---

## Entry points

Both entry points require the panel to be open. Dictation is a
panel-local action; it is not a global background capture.

### Panel button

A mic icon sits in the panel toolbar, to the right of the pin button.
Pressing it starts a dictation session into whichever buffer tab is
currently active.

### Panel hotkey

`Ctrl+Shift+D` starts dictation when the panel has focus. If the panel
is closed, the hotkey does nothing. This keeps the hotkey local to the
panel window and avoids registering a system-wide hook for dictation.

---

## Session lifecycle

### Start

- The mic button enters an active state (pulsing red dot).
- A single-utterance recogniser starts. It listens until natural silence
  is detected (the engine handles this; QuinSlate does not implement VAD).
- An 8-second hard timeout ends the session silently if no speech is
  detected. The timeout resets each time the user speaks.

### End (success)

- Transcribed text is appended to the buffer, preceded by a space if the
  buffer is non-empty and does not already end with whitespace.
- No punctuation is added unless the engine returns it.
- The mic button returns to its idle state.

### End (no speech / timeout)

- Buffer is unchanged.
- Mic button returns to idle state silently. No error toast.

### End (error)

- Buffer is unchanged.
- Mic button returns to idle state.
- A brief tray notification reads "Dictation unavailable." Log the error
  to the existing diagnostic log; do not surface the exception to the user.

---

## Stopping and cancelling

These are two distinct actions.

### Stop (commit)

Pressing the mic button again, or pressing `Ctrl+Shift+D`, stops
recording and commits whatever the engine has recognised so far. If the
engine has a partial result, it is appended. If nothing was recognised
yet, the session ends silently with no change to the buffer.

### Cancel (discard)

Pressing `Escape` cancels the session. Buffer is unchanged regardless
of what was recognised. No confirmation needed.

Closing or hiding the panel mid-session also cancels silently.

---

## Append behaviour

Dictated text appends, never replaces. This is consistent with how
capture without opening works. If the user wants to replace content, they
edit the buffer directly.

There is no undo specific to dictation. Standard text undo (`Ctrl+Z`)
in the editor applies.

---

## Punctuation

The `Windows.Media.SpeechRecognition` engine supports spoken punctuation
commands ("period", "new line", etc.) but this is off by default and
requires explicit grammar configuration. **Do not enable in v1.** Users
who need punctuation can add it manually. Add a settings toggle in v2 if
requested.

---

## Settings

No new settings in v1. The dictation timeout (15 s) is hardcoded.

If a settings panel is introduced later, expose:

- Timeout duration
- Spoken punctuation on/off
- Engine selection (inbox / Whisper) once Whisper is supported

---

## Out of scope for v1

- Continuous dictation (open mic, multiple utterances)
- Whisper.net backend
- Spoken punctuation
- Language/locale selection (uses the system default)
- Audio level indicator
- Post-dictation edit confirmation ("Did you mean…")
