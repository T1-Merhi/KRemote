using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using KRemote.Models;

namespace KRemote.Net;

/// <summary>
/// The receiving half of the app. Listens on <see cref="Protocol.Port"/>,
/// answers scan probes with this machine's name, and raises
/// <see cref="MessageReceived"/> for every text frame that arrives.
///
/// The event fires on a thread-pool thread; the UI is responsible for
/// marshalling it onto the dispatcher.
/// </summary>
public sealed class PeerServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, Protocol.Port);
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public event Action<TextMessage>? MessageReceived;

    /// <summary>
    /// Binds the port and starts accepting. Throws <see cref="SocketException"/>
    /// when the port is already taken -- typically a second KRemote instance on
    /// the same PC -- which the caller surfaces instead of crashing, because
    /// sending still works without a listener.
    /// </summary>
    public void Start()
    {
        _listener.Start();
        _started = true;
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { return; }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));

                var line = await reader.ReadLineAsync(timeout.Token);
                if (line is null) return;

                var frame = Protocol.Deserialize(line);
                if (frame is null) return;

                switch (frame.Type)
                {
                    case "ping":
                        await writer.WriteLineAsync(Protocol.Serialize(Frame.Pong(Environment.MachineName)));
                        break;

                    case "text":
                        var message = new TextMessage
                        {
                            From = string.IsNullOrWhiteSpace(frame.Name) ? remote : frame.Name!,
                            FromAddress = remote,
                            ReceivedAt = DateTime.Now,
                            Text = frame.Text ?? ""
                        };
                        await writer.WriteLineAsync(Protocol.Serialize(Frame.Ok()));
                        MessageReceived?.Invoke(message);
                        break;
                }
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_started) _listener.Stop();
        _cts.Dispose();
    }
}
