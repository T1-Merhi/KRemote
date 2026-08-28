using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using KRemote.Models;
using KRemote.Platform;

namespace KRemote.Net;

public sealed class PeerServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, Protocol.Port);
    private readonly CancellationTokenSource _cts = new();
    private readonly IDeviceIdentity _identity;
    private readonly IStoragePaths _paths;
    private readonly Func<AppSettings> _settings;
    private readonly Func<string> _currentPin;
    private bool _started;

    private sealed record PendingGroup(SessionMessage Message, int ExpectedCount)
    {
        public DateTime LastActivity = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, PendingGroup> _pendingGroups = new();

    public event Action<SessionMessage>? MessageReceived;

    public event Action<string, long, long>? TransferProgress;

    public PeerServer(
        IDeviceIdentity identity,
        IStoragePaths paths,
        Func<AppSettings>? settings = null,
        Func<string>? currentPin = null)
    {
        _identity = identity;
        _paths = paths;
        _settings = settings ?? (() => new AppSettings());
        _currentPin = currentPin ?? (() => _settings().Pin);
    }

    private string EffectiveDownloadDirectory
    {
        get
        {
            var configured = _settings().DownloadsFolder;
            return string.IsNullOrWhiteSpace(configured)
                ? _paths.DefaultReceivedFilesDirectory
                : configured;
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
                using var stream = client.GetStream();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(Protocol.StallTimeout);

                var line = await LineIO.ReadLineAsync(stream, timeout.Token);
                if (line is null) return;

                var frame = Protocol.Deserialize(line);
                if (frame is null) return;

                var settings = _settings();

                switch (frame.Type)
                {
                    case "ping":
                        var pong = Frame.Pong(_identity.MachineName, settings.DisplayName, settings.PinEnabled);
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

                        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ok()), timeout.Token);
                        MessageReceived?.Invoke(new SessionMessage
                        {
                            Kind = MessageKind.Text,
                            From = SenderName(frame, remote),
                            FromAddress = remote,
                            Title = frame.Title ?? "",
                            ReceivedAt = DateTime.Now,
                            Text = frame.Text ?? ""
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
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }
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
            MessageReceived?.Invoke(new SessionMessage
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
            new SessionMessage
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

    private async Task<SessionAttachment?> ReceiveOneFileAsync(
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
                    received += read;
                    TransferProgress?.Invoke(fileName, received, size);
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

        return new SessionAttachment { FileName = fileName, FilePath = finalPath, FileSize = size };
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
