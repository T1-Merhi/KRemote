using KRemote.Models;

namespace KRemote.Net;

public static class Discovery
{
    public static async Task RunAsync(
        IProgress<Peer>? found,
        IProgress<(int done, int total)>? progress,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gate = new object();

        var unique = new Progress<Peer>(peer =>
        {
            lock (gate)
            {
                if (!seen.Add(peer.Address)) return;
            }

            found?.Report(peer);
        });

        var beacon = PeerBeacon.DiscoverAsync(unique, PeerBeacon.DiscoveryWindow, ct);
        var sweep = PeerScanner.SweepAsync(unique, progress, ct);

        await Task.WhenAll(beacon, sweep);
    }
}
