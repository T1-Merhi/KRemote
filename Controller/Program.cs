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
// Threading note: low-level hook callbacks and WM_INPUT are both delivered
// on the thread that installed them -- this thread, the one running
// Application.Run -- so the key/button tracking sets below need no locking.
// Only the outbound queue crosses threads.
//
// Run:  dotnet run <receiver-ip>   (e.g. dotnet run 192.168.1.50)

class Program
{
    const int PORT = 5555;
    const int VK_F9 = 0x78;

    static NetworkStream? stream;
    static volatile bool capturing = false;

    // What we have told the receiver is currently held down, and what is
    // physically held down on this laptop while we are NOT capturing.
    // On every toggle the side that loses control has its held keys and
    // buttons released, otherwise a modifier held across the toggle stays
    // stuck down forever on the machine that just went idle.
    static readonly Dictionary<int, bool> remoteKeys = new(); // vk -> extended
    static readonly HashSet<string> remoteButtons = new();
    static readonly Dictionary<int, bool> localKeys = new();  // vk -> extended
    static readonly HashSet<string> localButtons = new();

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

    // ---------- capture toggle ----------

    static void ToggleCapture()
    {
        if (capturing)
        {
            // Control returns to this laptop: release what the receiver holds.
            // Must run while capturing is still true so Post() is still live.
            foreach (var kv in remoteKeys) Post($"KEY {kv.Key} up{(kv.Value ? " ext" : "")}");
            remoteKeys.Clear();
            foreach (var b in remoteButtons) Post($"BTN {b} up");
            remoteButtons.Clear();

            capturing = false;
        }
        else
        {
            // Control moves to the receiver: release what this laptop holds.
            // Must run while capturing is still false, otherwise the mouse and
            // keyboard hooks swallow our own injected key-up events.
            foreach (var kv in localKeys) LocalInput.SendKey((ushort)kv.Key, down: false, extended: kv.Value);
            localKeys.Clear();
            foreach (var b in localButtons) LocalInput.MouseButton(b, down: false);
            localButtons.Clear();

            capturing = true;
        }

        Console.WriteLine(capturing ? "-> Controlling REMOTE laptop" : "-> Controlling THIS laptop");
    }

    // ---------- input capture ----------

    static void OnRawMouse(int dx, int dy, ushort buttonFlags, short buttonData)
    {
        if (!capturing) return;

        PostMove(dx, dy);

        SendButton(buttonFlags, RawMouse.RI_MOUSE_LEFT_BUTTON_DOWN, "left", true);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_LEFT_BUTTON_UP, "left", false);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_RIGHT_BUTTON_DOWN, "right", true);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_RIGHT_BUTTON_UP, "right", false);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_MIDDLE_BUTTON_DOWN, "middle", true);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_MIDDLE_BUTTON_UP, "middle", false);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_BUTTON_4_DOWN, "x1", true);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_BUTTON_4_UP, "x1", false);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_BUTTON_5_DOWN, "x2", true);
        SendButton(buttonFlags, RawMouse.RI_MOUSE_BUTTON_5_UP, "x2", false);

        if ((buttonFlags & RawMouse.RI_MOUSE_WHEEL) != 0) Post($"WHEEL {buttonData}");
        if ((buttonFlags & RawMouse.RI_MOUSE_HWHEEL) != 0) Post($"HWHEEL {buttonData}");
    }

    static void SendButton(ushort flags, ushort mask, string name, bool down)
    {
        if ((flags & mask) == 0) return;
        if (down) remoteButtons.Add(name); else remoteButtons.Remove(name);
        Post($"BTN {name} {(down ? "down" : "up")}");
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

            // Swallow F9 in both directions. Forwarding only the down would
            // leave the receiver seeing a key-up it never saw pressed.
            if (vkCode == VK_F9)
            {
                if (isDown) ToggleCapture();
                return (IntPtr)1;
            }

            if (capturing)
            {
                string ext = extended ? " ext" : "";
                if (isDown) { remoteKeys[vkCode] = extended; Post($"KEY {vkCode} down{ext}"); }
                if (isUp) { remoteKeys.Remove(vkCode); Post($"KEY {vkCode} up{ext}"); }
                return (IntPtr)1; // swallow locally
            }

            // Not capturing: just remember what is held, so we can release it
            // on this machine when control moves to the receiver.
            if (isDown) localKeys[vkCode] = extended;
            if (isUp) localKeys.Remove(vkCode);
        }
        return LowLevelHooks.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // While capturing, the mouse hook is only a blocker: it stops local input
    // from reaching this laptop. All movement and button data comes from Raw
    // Input. While not capturing it tracks which buttons are held locally.
    static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if (capturing) return (IntPtr)1;

            switch ((int)wParam)
            {
                case LowLevelHooks.WM_LBUTTONDOWN: localButtons.Add("left"); break;
                case LowLevelHooks.WM_LBUTTONUP: localButtons.Remove("left"); break;
                case LowLevelHooks.WM_RBUTTONDOWN: localButtons.Add("right"); break;
                case LowLevelHooks.WM_RBUTTONUP: localButtons.Remove("right"); break;
                case LowLevelHooks.WM_MBUTTONDOWN: localButtons.Add("middle"); break;
                case LowLevelHooks.WM_MBUTTONUP: localButtons.Remove("middle"); break;
                case LowLevelHooks.WM_XBUTTONDOWN:
                case LowLevelHooks.WM_XBUTTONUP:
                {
                    var hs = Marshal.PtrToStructure<LowLevelHooks.MSLLHOOKSTRUCT>(lParam);
                    string name = ((hs.mouseData >> 16) & 0xffff) == 1 ? "x1" : "x2";
                    if ((int)wParam == LowLevelHooks.WM_XBUTTONDOWN) localButtons.Add(name);
                    else localButtons.Remove(name);
                    break;
                }
            }
        }
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

// ===================== Local input injection (release stuck keys) =====================
static class LocalInput
{
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx; public int dy; public uint mouseData;
        public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }

    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_XUP = 0x0100;
    const uint XBUTTON1 = 0x0001;
    const uint XBUTTON2 = 0x0002;

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void SendKey(ushort vk, bool down, bool extended)
    {
        uint flags = down ? 0u : KEYEVENTF_KEYUP;
        if (extended) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void MouseButton(string button, bool down)
    {
        if (down) return; // only ever used to release

        uint flag;
        uint data = 0;
        switch (button)
        {
            case "left": flag = MOUSEEVENTF_LEFTUP; break;
            case "right": flag = MOUSEEVENTF_RIGHTUP; break;
            case "middle": flag = MOUSEEVENTF_MIDDLEUP; break;
            case "x1": flag = MOUSEEVENTF_XUP; data = XBUTTON1; break;
            case "x2": flag = MOUSEEVENTF_XUP; data = XBUTTON2; break;
            default: return;
        }

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { mouseData = data, dwFlags = flag } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
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

    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;

    public const uint LLKHF_EXTENDED = 0x01;

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
