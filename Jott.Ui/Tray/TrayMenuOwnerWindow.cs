using Jott.Ui.Interop;
using System;
using System.Runtime.InteropServices;

namespace Jott.Ui.Tray;

/// <summary>
/// A hidden zero-sized Win32 popup window used solely as the owner / foreground
/// target for the tray context menu. <see cref="NativeMethods.TrackPopupMenu"/>
/// requires its owner to be the foreground window so the menu dismisses when
/// the user clicks outside it. Routing the
/// <see cref="NativeMethods.SetForegroundWindow"/> call to a dedicated invisible
/// window prevents the visible application window from being activated as a
/// side effect of opening the tray menu.
/// </summary>
public sealed class TrayMenuOwnerWindow : IDisposable
{
    private const string ClassName = "JottTrayMenuOwner";
    private const int OffscreenX = -32000;
    private const int OffscreenY = -32000;

    private readonly NativeMethods.WndProcDelegate wndProcDelegate;
    private readonly IntPtr moduleHandle;
    private ushort classAtom;
    private IntPtr windowHandle;
    private bool disposed;

    /// <summary>
    /// The HWND used as the menu's owner and foreground target. Always non-zero
    /// after construction; the window is invisible and not enumerable.
    /// </summary>
    public IntPtr Handle => windowHandle;

    /// <summary>
    /// Registers the window class and creates the hidden owner window. The
    /// window is shown without activation so subsequent
    /// <see cref="NativeMethods.SetForegroundWindow"/> calls on it succeed.
    /// </summary>
    public TrayMenuOwnerWindow()
    {
        moduleHandle = NativeMethods.GetModuleHandle(null);
        wndProcDelegate = DefaultWndProc;

        var wc = new NativeMethods.WNDCLASSEX();
        wc.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX));
        wc.lpfnWndProc = wndProcDelegate;
        wc.hInstance = moduleHandle;
        wc.lpszClassName = ClassName;
        classAtom = NativeMethods.RegisterClassEx(ref wc);

        uint exStyle = NativeMethods.WS_EX_TOOLWINDOW;
        windowHandle = NativeMethods.CreateWindowEx(
            exStyle,
            ClassName,
            string.Empty,
            NativeMethods.WS_POPUP,
            OffscreenX,
            OffscreenY,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            moduleHandle,
            IntPtr.Zero);

        if (windowHandle != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(windowHandle, NativeMethods.SW_SHOWNOACTIVATE);
        }
    }

    private static IntPtr DefaultWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (windowHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(windowHandle);
            windowHandle = IntPtr.Zero;
        }

        if (classAtom != 0)
        {
            NativeMethods.UnregisterClass(ClassName, moduleHandle);
            classAtom = 0;
        }
    }
}
