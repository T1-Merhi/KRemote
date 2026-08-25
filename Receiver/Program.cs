using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

// ===================== Receiver (runs on the SECOND laptop) =====================
// Listens on a TCP port, receives simple text commands, and injects them
// as real keyboard/mouse input using the Windows SendInput API.
//
// Protocol (one line per event, space-separated):
//   MOVE dx dy            -> move mouse cursor by relative delta
//   BTN left|right|middle down|up
//   WHEEL delta
//   KEY <vkCode> down|up
//
// Run:  dotnet run   (listens on 0.0.0.0:5555 by default)

class Program
{
    const int PORT = 5555;

    static void Main()
    {
        var listener = new TcpListener(IPAddress.Any, PORT);
        listener.Start();
        Console.WriteLine($"Receiver listening on port {PORT}. Waiting for controller...");

        while (true)
        {
            using var client = listener.AcceptTcpClient();
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
                    Win32Input.MouseWheel(delta);
                break;

            case "KEY":
                if (parts.Length >= 3 && ushort.TryParse(parts[1], out ushort vk))
                    Win32Input.SendKey(vk, parts[2] == "down");
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

    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_WHEEL = 0x0800;

    const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void MoveMouseRelative(int dx, int dy)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void MouseButton(string button, bool down)
    {
        uint flag = (button, down) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN,
            ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0u
        };
        if (flag == 0) return;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void MouseWheel(int delta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = MOUSEEVENTF_WHEEL } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void SendKey(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0u : KEYEVENTF_KEYUP } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
    }
}
