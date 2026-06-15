using Serilog;
using System;

namespace QuinSlate.Ui.Interop;

/// <summary>
/// Wraps <c>RegisterHotKey</c> / <c>UnregisterHotKey</c> for a single global hotkey.
/// The hotkey message (<see cref="NativeMethods.WM_HOTKEY"/>) is delivered to the
/// window's message loop; consumers must subclass the <c>WndProc</c> to observe it.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB001;

    private readonly IntPtr windowHandle;
    private bool registered;
    private bool disposed;

    /// <summary>
    /// Creates a manager for hotkeys delivered to <paramref name="windowHandle"/>.
    /// </summary>
    /// <param name="windowHandle">The HWND that will receive <c>WM_HOTKEY</c> messages.</param>
    public HotkeyManager(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        this.windowHandle = windowHandle;
    }

    /// <summary>
    /// The hotkey identifier used by <see cref="NativeMethods.WM_HOTKEY"/> in <c>wParam</c>.
    /// </summary>
    public int HotkeyIdentifier => HotkeyId;

    /// <summary>
    /// Registers the global hotkey (Ctrl+Shift+Q).
    /// Returns <c>false</c> if registration failed (for example a conflict with another app).
    /// Failures are logged and never throw or surface a dialog.
    /// </summary>
    public bool RegisterDefaultHotkey()
    {
        if (registered)
        {
            return true;
        }

        var modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        var result = NativeMethods.RegisterHotKey(windowHandle, HotkeyId, modifiers, (uint)NativeMethods.VK_Q);
        if (result == false)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.ForContext<HotkeyManager>().Warning("RegisterHotKey failed for Ctrl+Shift+Q. Win32 error: {Win32Error}", error);
            return false;
        }

        registered = true;
        Log.ForContext<HotkeyManager>().Information("Global hotkey Ctrl+Shift+Q registered.");
        return true;
    }

    /// <summary>
    /// Unregisters the hotkey if it was previously registered.
    /// </summary>
    public void Unregister()
    {
        if (registered == false)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(windowHandle, HotkeyId);
        registered = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Unregister();
        disposed = true;
    }
}
