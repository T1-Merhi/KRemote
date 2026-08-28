using System.Net;
using System.Net.Sockets;
using System.Text;
using KRemote.Models;
using KRemote.Platform;

namespace KRemote.Net;

public sealed class PeerBeacon : IDisposable
{
    public static readonly TimeSpan DiscoveryWindow = TimeSpan.FromMilliseconds(900);

    private readonly IDeviceIdentity _identity;
    private readonly Func<AppSettings> _settings;
    private readonly CancellationTokenSource _cts = new();

    private UdpClient? _listener;
    private IDisposable? _lease;

    public PeerBeacon(IDeviceIdentity identity, Func<AppSettings> settings)
    {
        _identity = identity;
        _settings = settings;
    }

    public void Start()
    {
        _lease = BroadcastLease.Acquire();

        var socket = new UdpClient();
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.DiscoveryPort));
        socket.EnableBroadcast = true;

        _listener = socket;
        _ = Task.Run(RespondLoopAsync);
    }

    private async Task RespondLoopAsync()
    {
        var socket = _listener;
        if (socket is null) return;

        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try { received = await socket.ReceiveAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            try
            {
                var frame = Protocol.Deserialize(Encoding.UTF8.GetString(received.Buffer));
                if (frame?.Type != "discover") continue;

                var settings = _settings();
                var pong = Frame.Pong(_identity.MachineName, settings.DisplayName, settings.PinEnabled);
                var payload = Encoding.UTF8.GetBytes(Protocol.Serialize(pong));

                await socket.SendAsync(payload, received.RemoteEndPoint, _cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { }
        }
    }

    public static async Task DiscoverAsync(
        IProgress<Peer>? found, TimeSpan window, CancellationToken ct)
    {
        using var lease = BroadcastLease.Acquire();
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };

        var own = LocalNetwork.OwnAddresses();
        var payload = Encoding.UTF8.GetBytes(Protocol.Serialize(Frame.Discover()));

        foreach (var target in LocalNetwork.BroadcastAddresses())
        {
            try { await socket.SendAsync(payload, new IPEndPoint(target, Protocol.DiscoveryPort), ct); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { return; }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(window);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!deadline.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try { received = await socket.ReceiveAsync(deadline.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            var address = received.RemoteEndPoint.Address.ToString();
            if (own.Contains(address)) continue;
            if (!seen.Add(address)) continue;

            var frame = Protocol.Deserialize(Encoding.UTF8.GetString(received.Buffer));
            if (frame?.Type != "pong") continue;

            found?.Report(new Peer
            {
                MachineName = string.IsNullOrWhiteSpace(frame.Name) ? address : frame.Name!,
                Address = address,
                DisplayName = string.IsNullOrWhiteSpace(frame.DisplayName) ? null : frame.DisplayName,
                IsProtected = frame.Protected == true
            });
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Dispose();
        _lease?.Dispose();
        _cts.Dispose();
    }
}
