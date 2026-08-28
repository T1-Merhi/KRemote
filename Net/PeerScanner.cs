using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using KRemote.Models;

namespace KRemote.Net;

public static class PeerScanner
{
    private const int ConnectTimeoutMs = 500;
    private const int HandshakeTimeoutMs = 800;
    private const int MaxConcurrentProbes = 128;

    public static async Task<List<Peer>> ScanAsync(
        IProgress<(int done, int total)>? progress,
        CancellationToken ct)
    {
        var (candidates, ownAddresses) = BuildCandidates();
        var found = new List<Peer>();
        var gate = new SemaphoreSlim(MaxConcurrentProbes);
        var done = 0;
        var total = candidates.Count;

        progress?.Report((0, total));

        var probes = candidates.Select(async ip =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var peer = await ProbeAsync(ip, ct);
                if (peer is not null)
                {
                    lock (found) found.Add(peer);
                }
            }
            finally
            {
                gate.Release();
                progress?.Report((Interlocked.Increment(ref done), total));
            }
        });

        await Task.WhenAll(probes);

        return found
            .Where(p => !ownAddresses.Contains(p.Address))
            .OrderBy(p => p.MachineName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (List<IPAddress> candidates, HashSet<string> own) BuildCandidates()
    {
        var candidates = new List<IPAddress>();
        var seen = new HashSet<string>();
        var own = new HashSet<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            foreach (var info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(info.Address)) continue;

                var octets = info.Address.GetAddressBytes();
                own.Add(info.Address.ToString());

                for (var host = 1; host <= 254; host++)
                {
                    var candidate = new IPAddress([octets[0], octets[1], octets[2], (byte)host]);
                    if (seen.Add(candidate.ToString())) candidates.Add(candidate);
                }
            }
        }

        return (candidates, own);
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
