using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// ===================== Controller (runs on the MAIN laptop) =====================
// Hooks keyboard + mouse globally. Press F9 to toggle "capture mode":
// while ON, your input is swallowed locally and streamed to the receiver
// laptop instead. Press F9 again to give control back to this laptop.
//
// Mouse movement comes from the Raw Input API (WM_INPUT), NOT from the
// low-level hook's cursor coordinates. The hook's MSLLHOOKSTRUCT.pt is an
// absolute screen point: it is clamped to the desktop bounds and it stops
// advancing once we swallow the move, so differencing it cannot produce a
// usable delta. Raw Input reports the device's own relative counts, which
// are immune to clamping, pointer speed and acceleration.
//
// Run:  dotnet run <receiver-ip>   (e.g. dotnet run 192.168.1.50)

class Program
{
    const int PORT = 5555;
    const int VK_F9 = 0x78;

    static NetworkStream? stream;
    static volatile bool capturing = false;

    // Outbound event queue. The hook and WM_INPUT handlers only enqueue;
    // a background thread does the socket writes. A low-level hook that
    // blocks for longer than LowLevelHooksTimeout is silently unhooked by
    // Windows, so we must never call into the socket from inside one.
    struct Evt
    {
        public bool IsMove;
        public int Dx, Dy;
        public string? Line;
    }

    static readonly List<Evt> outQ = new();

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
        var client = new TcpClient();
        client.NoDelay = true; // without this Nagle batches move packets and motion stutters
        client.Connect(ip, PORT);
        stream = client.GetStream();
        Console.WriteLine("Connected. Press F9 to toggle control of the other laptop. Ctrl+C here to quit.");

        new Thread(SenderLoop) { IsBackground = true, Name = "sender" }.Start();

        LowLevelHooks.SetHook(LowLevelHooks.WH_KEYBOARD_LL, kbProc);
        LowLevelHooks.SetHook(LowLevelHooks.WH_MOUSE_LL, mouseProc);

        RawMouse.Start(OnRawMouse); // creates the message-only window + registers for WM_INPUT

        Application.Run(); // message loop required for both hooks and WM_INPUT
    }

    // ---------- outbound queue ----------

    static void Post(string line)
    {
        lock (outQ)
        {
            outQ.Add(new Evt { Line = line });
            Monitor.Pulse(outQ);
        }
    }

    static void PostMove(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        lock (outQ)
        {
            // Merge into the tail if it is also a move, so a burst of 1000Hz
            // mouse reports collapses into one MOVE per network flush instead
            // of flooding the socket and lagging behind the user's hand.
            if (outQ.Count > 0)
            {
                var tail = outQ[^1];
                if (tail.IsMove)
                {
                    tail.Dx += dx;
                    tail.Dy += dy;
                    outQ[^1] = tail;
                    Monitor.Pulse(outQ);
                    return;
                }
            }
            outQ.Add(new Evt { IsMove = true, Dx = dx, Dy = dy });
            Monitor.Pulse(outQ);
        }
    }

    static void SenderLoop()
    {
        var batch = new List<Evt>();
        var sb = new StringBuilder();

        while (true)
        {
            lock (outQ)
            {
                while (outQ.Count == 0) Monitor.Wait(outQ);
                batch.AddRange(outQ);
                outQ.Clear();
            }

            sb.Clear();
            foreach (var e in batch)
            {
                if (e.IsMove) sb.Append("MOVE ").Append(e.Dx).Append(' ').Append(e.Dy).Append('\n');
                else sb.Append(e.Line).Append('\n');
            }
            batch.Clear();

            try
            {
                var bytes = Encoding.ASCII.GetBytes(sb.ToString());
                stream!.Write(bytes, 0, bytes.Length);
            }
            catch { /* ignore transient send errors */ }
        }
    }

    // ---------- input capture ----------

    static void OnRawMouse(int dx, int dy, ushort buttonFlags, short buttonData)
    {
        if (!capturing) return;

        PostMove(dx, dy);

        if ((buttonFlags & RawMouse.RI_MOUSE_LEFT_BUTTON_DOWN) != 0) Post("BTN left down");
        if ((buttonFlags & RawMouse.RI_MOUSE_LEFT_BUTTON_UP) != 0) Post("BTN left up");
        if ((buttonFlags & RawMouse.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0) Post("BTN right down");
        if ((buttonFlags & RawMouse.RI_MOUSE_RIGHT_BUTTON_UP) != 0) Post("BTN right up");
        if ((buttonFlags & RawMouse.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) Post("BTN middle down");
        if ((buttonFlags & RawMouse.RI_MOUSE_MIDDLE_BUTTON_UP) != 0) Post("BTN middle up");
        if ((buttonFlags & RawMouse.RI_MOUSE_BUTTON_4_DOWN) != 0) Post("BTN x1 down");
        if ((buttonFlags & RawMouse.RI_MOUSE_BUTTON_4_UP) != 0) Post("BTN x1 up");
        if ((buttonFlags & RawMouse.RI_MOUSE_BUTTON_5_DOWN) != 0) Post("BTN x2 down");
        if ((buttonFlags & RawMouse.RI_MOUSE_BUTTON_5_UP) != 0) Post("BTN x2 up");
        if ((buttonFlags & RawMouse.RI_MOUSE_WHEEL) != 0) Post($"WHEEL {buttonData}");
        if ((buttonFlags & RawMouse.RI_MOUSE_HWHEEL) != 0) Post($"HWHEEL {buttonData}");
    }

    static IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<LowLevelHooks.KBDLLHOOKSTRUCT>(lParam);
            int vkCode = (int)info.vkCode;
            bool extended = (info.flags & LowLevelHooks.LLKHF_EXTENDED) != 0;
            bool isDown = wParam == (IntPtr)LowLevelHooks.WM_KEYDOWN || wParam == (IntPtr)LowLevelHooks.WM_SYSKEYDOWN;
            bool isUp = wParam == (IntPtr)LowLevelHooks.WM_KEYUP || wParam == (IntPtr)LowLevelHooks.WM_SYSKEYUP;

            if (vkCode == VK_F9 && isDown)
            {
                capturing = !capturing;
                Console.WriteLine(capturing ? "-> Controlling REMOTE laptop" : "-> Controlling THIS laptop");
                return (IntPtr)1; // swallow the toggle key itself
            }

            if (capturing)
            {
                string ext = extended ? " ext" : "";
                if (isDown) Post($"KEY {vkCode} down{ext}");
                if (isUp) Post($"KEY {vkCode} up{ext}");
                return (IntPtr)1; // swallow locally
            }
        }
        return LowLevelHooks.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // The mouse hook is now only a blocker: it stops local input from reaching
    // this laptop while capturing. All movement/button data comes from Raw Input.
    static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && capturing) return (IntPtr)1;
        return LowLevelHooks.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }
}

// ===================== Raw Input (true relative mouse deltas) =====================
static class RawMouse
{
    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    public const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    public const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    public const ushort RI_MOUSE_BUTTON_4_UP = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
    public const ushort RI_MOUSE_BUTTON_5_UP = 0x0200;
    public const ushort RI_MOUSE_WHEEL = 0x0400;
    public const ushort RI_MOUSE_HWHEEL = 0x0800;

    const int WM_INPUT = 0x00FF;
    const uint RID_INPUT = 0x10000003;
    const uint RIDEV_INPUTSINK = 0x00000100;
    const uint RIM_TYPEMOUSE = 0;
    const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    // Mirrors RAWMOUSE. _pad covers the 2 alignment bytes between usFlags and
    // the ulButtons union; usButtonFlags/usButtonData are the union's members.
    [StructLayout(LayoutKind.Sequential)]
    struct RAWMOUSE
    {
        public ushort usFlags;
        public ushort _pad;
        public ushort usButtonFlags;
        public ushort usButtonData;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    static Action<int, int, ushort, short>? callback;
    static readonly IntPtr buffer = Marshal.AllocHGlobal(256);
    static readonly uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
    static readonly int mouseOffset = Marshal.SizeOf<RAWINPUTHEADER>();

    // Some devices (VM guests, RDP, tablets) report absolute positions
    // instead of relative counts; difference them ourselves.
    static int lastAbsX, lastAbsY;
    static bool haveAbs;

    static Sink? sink;

    sealed class Sink : NativeWindow
    {
        public Sink()
        {
            // HWND_MESSAGE (-3): a message-only window, never shown.
            CreateHandle(new CreateParams { Parent = (IntPtr)(-3) });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) Handle(m.LParam);
            base.WndProc(ref m);
        }
    }

    public static void Start(Action<int, int, ushort, short> onEvent)
    {
        callback = onEvent;
        sink = new Sink();

        var dev = new RAWINPUTDEVICE[1];
        dev[0].usUsagePage = 0x01; // generic desktop
        dev[0].usUsage = 0x02;     // mouse
        dev[0].dwFlags = RIDEV_INPUTSINK; // keep receiving even when unfocused
        dev[0].hwndTarget = sink.Handle;

        if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            Console.WriteLine("WARNING: RegisterRawInputDevices failed: " + Marshal.GetLastWin32Error());
    }

    static void Handle(IntPtr hRawInput)
    {
        uint size = 0;
        if (GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0) return;
        if (size == 0 || size > 256) return;
        if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) != size) return;

        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
        if (header.dwType != RIM_TYPEMOUSE) return;

        var mouse = Marshal.PtrToStructure<RAWMOUSE>(buffer + mouseOffset);

        int dx = mouse.lLastX;
        int dy = mouse.lLastY;

        if ((mouse.usFlags & MOUSE_MOVE_ABSOLUTE) != 0)
        {
            if (haveAbs) { dx = mouse.lLastX - lastAbsX; dy = mouse.lLastY - lastAbsY; }
            else { dx = 0; dy = 0; }
            lastAbsX = mouse.lLastX;
            lastAbsY = mouse.lLastY;
            haveAbs = true;
        }

        callback?.Invoke(dx, dy, mouse.usButtonFlags, unchecked((short)mouse.usButtonData));
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

    public const uint LLKHF_EXTENDED = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
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
