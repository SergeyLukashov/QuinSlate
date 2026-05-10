# SPEC: Capture without opening

## What
Ctrl+Shift+1 through Ctrl+Shift+7 silently append the current text
selection from any app to the matching buffer. The panel never opens.
A tray notification confirms the capture.

## Hotkeys

Register 7 hotkeys on startup:

    Ctrl+Shift+1  →  append to buffer 1
    ...
    Ctrl+Shift+7  →  append to buffer 7

Use `RegisterHotKey` with a stable ID set (e.g. 0x1001–0x1007).
If a specific slot fails to register (conflict), log it and continue —
the remaining slots must still register. Do not block startup.

## Capture sequence

Execute in this exact order:

1. **Guard — non-text clipboard**: call `IsClipboardFormatAvailable(CF_TEXT)`.
   If false, abort silently. Do not touch the clipboard.

2. **Save current clipboard**: open the clipboard with `OpenClipboard`,
   call `GetClipboardData` for all available formats, store the raw
   handles. Close the clipboard.

3. **Send Ctrl+C** to the foreground window using `SendInput` with
   virtual key `VK_C` + `CTRL` modifier.

4. **Wait for clipboard update**: listen for `WM_CLIPBOARDUPDATE`
   (register via `AddClipboardFormatListener`). Wait up to 100 ms. If
   the message does not arrive within the timeout, proceed anyway with a
   direct clipboard read (some apps skip the notification).

5. **Read new clipboard text**: call `Clipboard.GetText()`.
   - If the result is empty, abort. Do not append a blank line.
   - If the result is identical to the clipboard content from step 2,
     the foreground window did not copy anything — abort silently.

6. **Append to buffer**: read the current contents of `buffer-N.txt`.
   If the file is non-empty and does not end with a newline, prepend
   `\n` before the captured text. Write the updated content back.
   Bypass the normal debounce — write immediately.

7. **Restore clipboard**: reopen the clipboard and restore all formats
   saved in step 2.

8. **Confirm with tray notification**: show a `NIF_INFO` balloon:

       Title:   Jott
       Body:    Captured to buffer N
       Timeout: 2000 ms

## Error handling

| Condition | Action |
|---|---|
| `OpenClipboard` fails (locked) | Retry once after 30 ms. If still locked, abort silently. |
| `SendInput` succeeds but clipboard unchanged after 100 ms | Abort silently. |
| Buffer file write fails | Show tray notification: "Capture failed — could not write to buffer N." |
| All 7 hotkeys fail to register | Log error on startup. No dialog. |

## Threading

All clipboard operations must run on an STA thread. The WinUI 3 UI
thread is STA. Do not dispatch clipboard work to a background thread —
it will throw `COMException`.

## Notes

- `SendInput` injects at the hardware input level and reliably triggers
  copy in virtually all apps including UAC-elevated windows (unlike
  `PostMessage`/`SendMessage` which are blocked across privilege
  boundaries).
- Test with: a UAC-elevated window, a game in exclusive fullscreen, a
  window with no selection active, a non-text control (e.g. image in
  a browser), and a window that copies asynchronously (e.g. some
  Electron apps).
