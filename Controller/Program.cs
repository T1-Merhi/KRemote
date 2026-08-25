using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// ===================== Controller (runs on the MAIN laptop) =====================
// Hooks keyboard + mouse globally. Press F9 to toggle "capture mode":
// while ON, your input is swallowed locally and streamed to the receiver
// laptop instead. Press F9 again to give control back to this laptop.
//
// Run:  dotnet run <receiver-ip>   (e.g. dotnet run 192.168.1.50)

class Program
{
    const int PORT = 5555;
    const int VK_F9 = 0x78;

    static TcpClient? client;
    static NetworkStream? stream;
    static bool capturing = false;
    static int lastX, lastY;
    static bool haveLast = false;

    static LowLevelHooks.HookProc kbProc = KeyboardHook;
    static LowLevelHooks.HookProc mouseProc = MouseHook;

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run <receiver-ip>");
            return;
        }
        string ip = args[0];

        Console.WriteLine($"Connecting to receiver at {ip}:{PORT} ...");
        client = new TcpClient();
        client.Connect(ip, PORT);
        stream = client.GetStream();
        Console.WriteLine("Connected. Press F9 to toggle control of the other laptop. Ctrl+C here to quit.");

        IntPtr hKb = LowLevelHooks.SetHook(LowLevelHooks.WH_KEYBOARD_LL, kbProc);
        IntPtr hMouse = LowLevelHooks.SetHook(LowLevelHooks.WH_MOUSE_LL, mouseProc);

        System.Windows.Forms.Application.Run(); // message loop required for hooks (see .csproj note)
    }

    static void Send(string line)
    {
        if (stream == null) return;
        try
        {
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }
        catch { /* ignore transient send errors */ }
    }

    static IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            bool isDown = wParam == (IntPtr)LowLevelHooks.WM_KEYDOWN || wParam == (IntPtr)LowLevelHooks.WM_SYSKEYDOWN;
            bool isUp = wParam == (IntPtr)LowLevelHooks.WM_KEYUP || wParam == (IntPtr)LowLevelHooks.WM_SYSKEYUP;

            if (vkCode == VK_F9 && isDown)
            {
                capturing = !capturing;
                haveLast = false;
                Console.WriteLine(capturing ? "-> Controlling REMOTE laptop" : "-> Controlling THIS laptop");
                return (IntPtr)1; // swallow the toggle key itself
            }

            if (capturing)
            {
                if (isDown) Send($"KEY {vkCode} down");
                if (isUp) Send($"KEY {vkCode} up");
                return (IntPtr)1; // swallow locally
            }
        }
        return LowLevelHooks.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && capturing)
        {
            var hookStruct = Marshal.PtrToStructure<LowLevelHooks.MSLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;

            if (msg == LowLevelHooks.WM_MOUSEMOVE)
            {
                if (haveLast)
                {
                    int dx = hookStruct.pt.x - lastX;
                    int dy = hookStruct.pt.y - lastY;
                    if (dx != 0 || dy != 0) Send($"MOVE {dx} {dy}");
                }
                lastX = hookStruct.pt.x;
                lastY = hookStruct.pt.y;
                haveLast = true;
            }
            else if (msg == LowLevelHooks.WM_LBUTTONDOWN) Send("BTN left down");
            else if (msg == LowLevelHooks.WM_LBUTTONUP) Send("BTN left up");
            else if (msg == LowLevelHooks.WM_RBUTTONDOWN) Send("BTN right down");
            else if (msg == LowLevelHooks.WM_RBUTTONUP) Send("BTN right up");
            else if (msg == LowLevelHooks.WM_MBUTTONDOWN) Send("BTN middle down");
            else if (msg == LowLevelHooks.WM_MBUTTONUP) Send("BTN middle up");
            else if (msg == LowLevelHooks.WM_MOUSEWHEEL)
            {
                int delta = (short)((hookStruct.mouseData >> 16) & 0xffff);
                Send($"WHEEL {delta}");
            }

            return (IntPtr)1; // swallow so it doesn't also move the local cursor
        }
        return LowLevelHooks.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}

// ===================== Low-level hook plumbing =====================
static class LowLevelHooks
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    public static IntPtr SetHook(int hookId, HookProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(hookId, proc, GetModuleHandle(curModule.ModuleName), 0);
    }
}
