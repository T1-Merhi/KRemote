using System.IO;
using System.Text;

namespace KRemote.Net;

public static class LineIO
{
    private const int MaxLineBytes = 64 * 1024;

    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream(256);
        var one = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (read == 0)
                return buffer.Length == 0 ? null : Decode(buffer);

            if (one[0] == (byte)'\n')
                return Decode(buffer);

            if (buffer.Length >= MaxLineBytes)
                throw new IOException($"Header exceeded {MaxLineBytes} bytes without a newline.");

            buffer.WriteByte(one[0]);
        }

        static string Decode(MemoryStream buffer)
        {
            var bytes = buffer.ToArray();
            var length = bytes.Length > 0 && bytes[^1] == (byte)'\r' ? bytes.Length - 1 : bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }
    }

    public static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }
}
