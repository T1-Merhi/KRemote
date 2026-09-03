using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Sockets;
using KRemote.Models;

namespace KRemote.Net;

public sealed class PeerServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, Protocol.Port);
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<AppSettings> _settings;
    private readonly Func<string> _currentPin;
    private bool _started;

    private sealed record PendingGroup(InboxMessage Message, int ExpectedCount)
    {
        public DateTime LastActivity = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, PendingGroup> _pendingGroups = new();

    public event Action<InboxMessage>? MessageReceived;

    public event Action<string, long, long>? TransferProgress;

    public PeerServer(Func<AppSettings>? settings = null, Func<string>? currentPin = null)
    {
        _settings = settings ?? (() => new AppSettings());
        _currentPin = currentPin ?? (() => _settings().Pin);
    }

    public static string DownloadDirectory
    {
        get
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            if (!Directory.Exists(downloads))
                downloads = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return Path.Combine(downloads, "KRemote");
        }
    }

    private string EffectiveDownloadDirectory
    {
        get
        {
            var configured = _settings().DownloadsFolder;
            return string.IsNullOrWhiteSpace(configured) ? DownloadDirectory : configured;
        }
    }

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
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                client.NoDelay = true;
                using var stream = client.GetStream();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(Protocol.StallTimeout);

                try
                {
                    await DispatchAsync(stream, remote, timeout);
                }
                catch (IOException e)
                {
                    try
                    {
                        await LineIO.WriteLineAsync(
                            stream, Protocol.Serialize(Frame.Refused(e.Message)), timeout.Token);
                    }
                    catch (Exception inner) when (
                        inner is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
                }

                await DrainCloseAsync(client, stream, timeout.Token);
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }
    }

    private async Task DispatchAsync(NetworkStream stream, string remote, CancellationTokenSource timeout)
    {
        var line = await LineIO.ReadLineAsync(stream, timeout.Token);
        if (line is null) return;

        var frame = Protocol.Deserialize(line);
        if (frame is null) return;

        var settings = _settings();

        switch (frame.Type)
        {
            case "ping":
                var pong = Frame.Pong(Environment.MachineName, settings.DisplayName, settings.PinEnabled);
                await LineIO.WriteLineAsync(stream, Protocol.Serialize(pong), timeout.Token);
                break;

            case "verifypin":
                var verifyReply = IsPinCorrect(frame, settings)
                    ? Frame.Ok()
                    : Frame.Refused("Incorrect PIN.");
                await LineIO.WriteLineAsync(stream, Protocol.Serialize(verifyReply), timeout.Token);
                break;

            case "text":
                if (!IsPinCorrect(frame, settings))
                {
                    await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Refused("Incorrect PIN.")), timeout.Token);
                    break;
                }

                var body = await ReadTextBodyAsync(stream, frame, timeout);
                if (body is null) break;

                await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ok()), timeout.Token);
                MessageReceived?.Invoke(new InboxMessage
                {
                    Kind = MessageKind.Text,
                    From = SenderName(frame, remote),
                    FromAddress = remote,
                    Title = frame.Title ?? "",
                    ReceivedAt = DateTime.Now,
                    Text = body
                });
                break;

            case "file":
                if (!IsPinCorrect(frame, settings))
                {
                    await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Refused("Incorrect PIN.")), timeout.Token);
                    break;
                }

                await ReceiveFileAsync(stream, frame, remote, timeout);
                break;
        }
    }

    private static async Task<string?> ReadTextBodyAsync(
        NetworkStream stream, Frame header, CancellationTokenSource timeout)
    {
        var size = header.Size ?? -1;
        if (size < 0 || size > Protocol.MaxTextBytes)
        {
            await LineIO.WriteLineAsync(
                stream, Protocol.Serialize(Frame.Refused("Missing or oversized text length.")), timeout.Token);
            return null;
        }

        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ready()), timeout.Token);

        var body = new byte[size];
        var received = 0;

        while (received < size)
        {
            timeout.CancelAfter(Protocol.StallTimeout);

            var read = await stream.ReadAsync(body.AsMemory(received, (int)(size - received)), timeout.Token);
            if (read == 0)
                throw new IOException($"Sender disconnected after {received:N0} of {size:N0} bytes.");

            received += read;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task DrainCloseAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        try
        {
            await stream.FlushAsync(ct);
            client.Client.Shutdown(SocketShutdown.Send);

            var scratch = new byte[256];
            while (await stream.ReadAsync(scratch, ct) > 0) { }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
    }

    private bool IsPinCorrect(Frame frame, AppSettings settings)
    {
        if (!settings.PinEnabled) return true;
        return !string.IsNullOrEmpty(frame.Pin) && frame.Pin == _currentPin();
    }

    private static string SenderName(Frame frame, string remote)
    {
        if (!string.IsNullOrWhiteSpace(frame.DisplayName)) return frame.DisplayName!.Trim();
        if (!string.IsNullOrWhiteSpace(frame.Name)) return frame.Name!;
        return remote;
    }

    private async Task ReceiveFileAsync(
        NetworkStream stream, Frame header, string remote, CancellationTokenSource timeout)
    {
        var attachment = await ReceiveOneFileAsync(stream, header, timeout);
        if (attachment is null) return;

        var from = SenderName(header, remote);

        if (header.GroupId is null)
        {
            MessageReceived?.Invoke(new InboxMessage
            {
                Kind = MessageKind.File,
                From = from,
                FromAddress = remote,
                Title = header.Title ?? "",
                ReceivedAt = DateTime.Now,
                FileName = attachment.FileName,
                FilePath = attachment.FilePath,
                FileSize = attachment.FileSize,
                Attachments = [attachment]
            });
            return;
        }

        SweepStaleGroups();

        var key = $"{remote}|{header.GroupId}";
        var expectedCount = Math.Max(1, header.GroupCount ?? 1);

        var pending = _pendingGroups.GetOrAdd(key, _ => new PendingGroup(
            new InboxMessage
            {
                Kind = MessageKind.File,
                From = from,
                FromAddress = remote,
                Title = header.Title ?? "",
                Text = header.Text ?? "",
                ReceivedAt = DateTime.Now,
                FileName = attachment.FileName,
                FilePath = attachment.FilePath,
                FileSize = attachment.FileSize
            },
            expectedCount));

        lock (pending)
        {
            pending.Message.Attachments.Add(attachment);
            pending.LastActivity = DateTime.UtcNow;

            if (pending.Message.Attachments.Count < pending.ExpectedCount) return;
        }

        _pendingGroups.TryRemove(key, out _);
        MessageReceived?.Invoke(pending.Message);
    }

    private void SweepStaleGroups()
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _settings().GroupTimeoutSeconds));
        var cutoff = DateTime.UtcNow - timeout;

        foreach (var (key, pending) in _pendingGroups)
        {
            if (pending.LastActivity > cutoff) continue;
            if (!_pendingGroups.TryRemove(key, out _)) continue;

            MessageReceived?.Invoke(pending.Message);
        }
    }

    private async Task<InboxAttachment?> ReceiveOneFileAsync(
        NetworkStream stream, Frame header, CancellationTokenSource timeout)
    {
        var size = header.Size ?? -1;
        if (size < 0)
        {
            await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Refused("Missing or negative file size.")), timeout.Token);
            return null;
        }

        var downloadDirectory = EffectiveDownloadDirectory;
        string finalPath;
        string partPath;
        try
        {
            Directory.CreateDirectory(downloadDirectory);
            finalPath = FileNaming.UniquePath(downloadDirectory, FileNaming.Sanitize(header.FileName, "received-file"));
            partPath = finalPath + ".part";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Refused($"Cannot write to the downloads folder: {e.Message}")), timeout.Token);
            return null;
        }

        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ready()), timeout.Token);

        var fileName = Path.GetFileName(finalPath);
        var received = 0L;

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var file = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                Protocol.ChunkSize, useAsync: true))
            {
                var buffer = new byte[Protocol.ChunkSize];
                TransferProgress?.Invoke(fileName, 0, size);

                while (received < size)
                {
                    timeout.CancelAfter(Protocol.StallTimeout);

                    var want = (int)Math.Min(buffer.Length, size - received);
                    var read = await stream.ReadAsync(buffer.AsMemory(0, want), timeout.Token);
                    if (read == 0)
                        throw new IOException($"Sender disconnected after {received:N0} of {size:N0} bytes.");

                    await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    hash.AppendData(buffer.AsSpan(0, read));
                    received += read;
                    TransferProgress?.Invoke(fileName, received, size);
                }
            }

            if (!string.IsNullOrEmpty(header.Sha256))
            {
                var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                var expected = header.Sha256.Trim().ToLowerInvariant();

                if (actual.Length != expected.Length ||
                    !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected)))
                {
                    TryDelete(partPath);
                    await LineIO.WriteLineAsync(
                        stream,
                        Protocol.Serialize(Frame.Refused($"{fileName} arrived corrupted (checksum mismatch).")),
                        timeout.Token);
                    return null;
                }
            }

            File.Move(partPath, finalPath, overwrite: false);
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
        finally
        {
            TransferProgress?.Invoke(fileName, received, size);
        }

        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ok()), timeout.Token);

        return new InboxAttachment { FileName = fileName, FilePath = finalPath, FileSize = size };
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_started) _listener.Stop();
        _cts.Dispose();
    }
}
