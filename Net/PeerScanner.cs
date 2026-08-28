using System.IO;
using System.Net;
using System.Net.Sockets;

using KRemote.Models;

namespace KRemote.Net;

public static class PeerScanner
{
    private const int ConnectTimeoutMs = 500;
    private const int HandshakeTimeoutMs = 800;
    private const int MaxConcurrentProbes = 128;

    public static async Task SweepAsync(
        IProgress<Peer>? found,
        IProgress<(int done, int total)>? progress,
        CancellationToken ct)
    {
        var candidates = LocalNetwork.SubnetCandidates();
        var own = LocalNetwork.OwnAddresses();

        var gate = new SemaphoreSlim(MaxConcurrentProbes);
        var done = 0;
        var total = candidates.Count;

        progress?.Report((0, total));

        var probes = candidates.Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (own.Contains(ip.ToString())) return;

                var peer = await ProbeAsync(ip, ct);
                if (peer is not null) found?.Report(peer);
            }
            finally
            {
                gate.Release();
                progress?.Report((Interlocked.Increment(ref done), total));
            }
        });

        await Task.WhenAll(probes);
    }

    private static async Task<Peer?> ProbeAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(ConnectTimeoutMs);
                await client.ConnectAsync(ip, Protocol.Port, connectCts.Token);
            }

            using var stream = client.GetStream();

            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(HandshakeTimeoutMs);

            await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ping()), handshakeCts.Token);
            var line = await LineIO.ReadLineAsync(stream, handshakeCts.Token);
            if (line is null) return null;

            var frame = Protocol.Deserialize(line);
            if (frame?.Type != "pong") return null;

            return new Peer
            {
                MachineName = string.IsNullOrWhiteSpace(frame.Name) ? ip.ToString() : frame.Name!,
                Address = ip.ToString(),
                DisplayName = string.IsNullOrWhiteSpace(frame.DisplayName) ? null : frame.DisplayName,
                IsProtected = frame.Protected == true
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (SocketException) { return null; }
        catch (IOException) { return null; }
        catch (ObjectDisposedException) { return null; }
    }
}
