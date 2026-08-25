using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

// ===================== Receiver (runs on the SECOND laptop) =====================
// Listens on a TCP port, receives simple text commands, and injects them
// as real keyboard/mouse input using the Windows SendInput API.
//
// Movement is applied as an ABSOLUTE move (current position + delta, then
// normalised to the 0..65535 virtual-desktop space). Relative SendInput
// moves are run through the receiver's pointer ballistics -- its pointer
// speed slider and "Enhance pointer precision" curve -- so the same delta
// would travel a different, non-linear distance here than it did on the
// controller. Absolute moves bypass that entirely: 1 pixel means 1 pixel.
//
// Protocol (one line per event, space-separated):
//   MOVE dx dy            -> move mouse cursor by relative delta
//   BTN left|right|middle|x1|x2 down|up
//   WHEEL delta
//   HWHEEL delta
//   KEY <vkCode> down|up [ext]
//
// Run:  dotnet run   (listens on 0.0.0.0:5555 by default)

class Program
{
    const int PORT = 5555;

    static void Main()
    {
        // Without this the process is DPI-virtualised on scaled displays and
        // GetCursorPos/GetSystemMetrics report fake 96-DPI coordinates, which
        // makes the absolute-move maths land in the wrong place.
        Win32Input.EnableDpiAwareness();

        var listener = new TcpListener(IPAddress.Any, PORT);
        listener.Start();
        Console.WriteLine($"Receiver listening on port {PORT}. Waiting for controller...");

        while (true)
        {
            using var client = listener.AcceptTcpClient();
            client.NoDelay = true;
            Console.WriteLine($"Connected: {client.Client.RemoteEndPoint}");
            HandleClient(client);
            Console.WriteLine("Disconnected. Waiting for new connection...");
        }
    }

    static void HandleClient(TcpClient client)
    {
        using var stream = client.GetStream();
        using var reader = new System.IO.StreamReader(stream, Encoding.ASCII);
        string? line;
        try
        {
            while ((line = reader.ReadLine()) != null)
            {
                ProcessCommand(line);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Connection error: " + ex.Message);
        }
    }

    static void ProcessCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        switch (parts[0])
        {
            case "MOVE":
                if (parts.Length >= 3 &&
                    int.TryParse(parts[1], out int dx) &&
                    int.TryParse(parts[2], out int dy))
                {
                    Win32Input.MoveMouseRelative(dx, dy);
                }
                break;

            case "BTN":
                if (parts.Length >= 3)
                    Win32Input.MouseButton(parts[1], parts[2] == "down");
                break;

            case "WHEEL":
                if (parts.Length >= 2 && int.TryParse(parts[1], out int delta))
                    Win32Input.MouseWheel(delta, horizontal: false);
                break;

            case "HWHEEL":
                if (parts.Length >= 2 && int.TryParse(parts[1], out int hdelta))
                    Win32Input.MouseWheel(hdelta, horizontal: true);
                break;

            case "KEY":
                if (parts.Length >= 3 && ushort.TryParse(parts[1], out ushort vk))
                {
                    bool extended = parts.Length >= 4 && parts[3] == "ext";
                    Win32Input.SendKey(vk, parts[2] == "down", extended);
                }
                break;
        }
    }
}

// ===================== Win32 input injection =====================
static class Win32Input
{
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x; public int y; }

    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_XDOWN = 0x0080;
    const uint MOUSEEVENTF_XUP = 0x0100;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_HWHEEL = 0x1000;
    const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    const uint XBUTTON1 = 0x0001;
    const uint XBUTTON2 = 0x0002;

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;

    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    public static void EnableDpiAwareness()
    {
        try { SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch { /* pre-1703 Windows: nothing we can do, carry on */ }
    }

    public static void MoveMouseRelative(int dx, int dy)
    {
        if (!GetCursorPos(out POINT p)) return;

        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vWidth <= 1 || vHeight <= 1) return;

        // Read the live cursor position every time rather than tracking our own,
        // so rounding never accumulates and the receiver's own mouse still works.
        int targetX = Math.Clamp(p.x + dx, vLeft, vLeft + vWidth - 1);
        int targetY = Math.Clamp(p.y + dy, vTop, vTop + vHeight - 1);

        int nx = (int)Math.Round((targetX - vLeft) * 65535.0 / (vWidth - 1));
        int ny = (int)Math.Round((targetY - vTop) * 65535.0 / (vHeight - 1));

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = nx,
                    dy = ny,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void MouseButton(string button, bool down)
    {
        uint flag;
        uint data = 0;

        switch (button)
        {
            case "left": flag = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
            case "right": flag = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
            case "middle": flag = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
            case "x1": flag = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON1; break;
            case "x2": flag = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON2; break;
            default: return;
        }

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { mouseData = data, dwFlags = flag } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void MouseWheel(int delta, bool horizontal)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

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
}
