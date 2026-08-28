using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KRemote.Notifications;

internal static class TaskbarFlash
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private const uint FLASHW_TRAY = 0x00000002;
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    public static void Flash(Window window)
    {
        if (window.IsActive) return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var info = new FLASHWINFO
        {
            hwnd = handle,
            dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0
        };
        info.cbSize = (uint)Marshal.SizeOf(info);

        FlashWindowEx(ref info);
    }
}
