using System.IO;
using System.Text;

namespace KRemote.Net;

/// <summary>
/// Reads and writes the protocol's newline-delimited JSON headers directly on
/// the socket stream.
///
/// A <see cref="StreamReader"/> cannot be used for this. It fills a buffer from
/// the socket, so reading the one-line header of a file transfer would pull the
/// first kilobytes of the file's binary payload into the reader's buffer, where
/// the code that streams the body would never see them. Reading a byte at a
/// time costs nothing on a header of a few dozen bytes and leaves the stream
/// positioned exactly after the newline.
/// </summary>
public static class LineIO
{
    /// <summary>Longest header we will accept, as a defense against a peer that never sends a newline.</summary>
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
            // Tolerate CRLF from anything that writes Windows line endings.
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
