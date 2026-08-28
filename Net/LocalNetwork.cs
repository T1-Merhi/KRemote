using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace KRemote.Net;

public static class LocalNetwork
{
    public static HashSet<string> OwnAddresses()
    {
        var own = new HashSet<string>();

        foreach (var (address, _) in ActiveIPv4Addresses())
            own.Add(address.ToString());

        return own;
    }

    public static List<IPAddress> BroadcastAddresses()
    {
        var results = new List<IPAddress> { IPAddress.Broadcast };
        var seen = new HashSet<string> { IPAddress.Broadcast.ToString() };

        foreach (var (address, mask) in ActiveIPv4Addresses())
        {
            if (mask is null) continue;

            var host = address.GetAddressBytes();
            var bits = mask.GetAddressBytes();
            if (host.Length != 4 || bits.Length != 4) continue;

            var directed = new byte[4];
            for (var i = 0; i < 4; i++)
                directed[i] = (byte)(host[i] | (byte)~bits[i]);

            var candidate = new IPAddress(directed);
            if (seen.Add(candidate.ToString())) results.Add(candidate);
        }

        return results;
    }

    public static List<IPAddress> SubnetCandidates()
    {
        var candidates = new List<IPAddress>();
        var seen = new HashSet<string>();

        foreach (var (address, _) in ActiveIPv4Addresses())
        {
            var octets = address.GetAddressBytes();

            for (var host = 1; host <= 254; host++)
            {
                var candidate = new IPAddress([octets[0], octets[1], octets[2], (byte)host]);
                if (seen.Add(candidate.ToString())) candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static IEnumerable<(IPAddress Address, IPAddress? Mask)> ActiveIPv4Addresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            IPInterfaceProperties properties;
            try { properties = nic.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            catch (PlatformNotSupportedException) { continue; }

            foreach (var info in properties.UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(info.Address)) continue;

                IPAddress? mask = null;
                try { mask = info.IPv4Mask; }
                catch (PlatformNotSupportedException) { }

                yield return (info.Address, mask);
            }
        }
    }
}
