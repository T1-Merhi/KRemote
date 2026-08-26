using System.IO;
using System.Net.Sockets;
using System.Text;

namespace KRemote.Net;

/// <summary>
/// The sending half: opens a connection to one chosen peer, writes a single
/// text frame and waits for its acknowledgement. Any failure is thrown so the
/// UI can say the delivery did not happen rather than silently pretending it
/// did.
/// </summary>
public static class PeerSender
{
    private const int TimeoutMs = 5000;

    public static async Task SendAsync(string address, string text, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeoutMs);

        using var client = new TcpClient();
        await client.ConnectAsync(address, Protocol.Port, cts.Token);

        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        var frame = Frame.TextMessage(Environment.MachineName, text);
        await writer.WriteLineAsync(Protocol.Serialize(frame).AsMemory(), cts.Token);

        var reply = await reader.ReadLineAsync(cts.Token);
        if (Protocol.Deserialize(reply ?? "")?.Type != "ok")
            throw new IOException("The other PC did not acknowledge the message.");
    }
}
