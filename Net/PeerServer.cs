using System.IO;
using System.Net;
using System.Net.Sockets;
using KRemote.Models;

namespace KRemote.Net;

/// <summary>
/// The receiving half of the app. Listens on <see cref="Protocol.Port"/>,
/// answers scan probes with this machine's name, and raises
/// <see cref="MessageReceived"/> for every text or file that arrives.
///
/// Events fire on thread-pool threads; the UI marshals them onto the
/// dispatcher.
/// </summary>
public sealed class PeerServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Any, Protocol.Port);
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public event Action<InboxMessage>? MessageReceived;

    /// <summary>Fires repeatedly while a file is arriving: name, bytes so far, total.</summary>
    public event Action<string, long, long>? TransferProgress;

    /// <summary>Where received files are written. Created on first use.</summary>
    public static string DownloadDirectory
    {
        get
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            // A profile without a Downloads folder is unusual but not impossible;
            // Documents always exists.
            if (!Directory.Exists(downloads))
                downloads = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return Path.Combine(downloads, "KRemote");
        }
    }

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
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                using var stream = client.GetStream();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(Protocol.StallTimeout);

                var line = await LineIO.ReadLineAsync(stream, timeout.Token);
                if (line is null) return;

                var frame = Protocol.Deserialize(line);
                if (frame is null) return;

                switch (frame.Type)
                {
                    case "ping":
                        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Pong(Environment.MachineName)), timeout.Token);
                        break;

                    case "text":
                        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ok()), timeout.Token);
                        MessageReceived?.Invoke(new InboxMessage
                        {
                            Kind = MessageKind.Text,
                            From = string.IsNullOrWhiteSpace(frame.Name) ? remote : frame.Name!,
                            FromAddress = remote,
                            Title = frame.Title ?? "",
                            ReceivedAt = DateTime.Now,
                            Text = frame.Text ?? ""
                        });
                        break;

                    case "file":
                        await ReceiveFileAsync(stream, frame, remote, timeout);
                        break;
                }
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }
    }

    private async Task ReceiveFileAsync(
        NetworkStream stream, Frame header, string remote, CancellationTokenSource timeout)
    {
        var size = header.Size ?? -1;
        if (size < 0)
        {
            await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Refused("Missing or negative file size.")), timeout.Token);
            return;
        }

        string finalPath;
        string partPath;
        try
        {
            Directory.CreateDirectory(DownloadDirectory);
            finalPath = UniquePath(DownloadDirectory, SafeFileName(header.FileName));
            partPath = finalPath + ".part";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Refused($"Cannot write to the downloads folder: {e.Message}")), timeout.Token);
            return;
        }

        // Only now does the sender start pushing bytes.
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
                    // Refresh the stall deadline per chunk: the limit is on
                    // silence, not on how long a large transfer may take.
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
            // A half-written .part file is worse than nothing: it looks like a
            // real download until you open it.
            TryDelete(partPath);
            throw;
        }
        finally
        {
            TransferProgress?.Invoke(fileName, received, size);
        }

        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.Ok()), timeout.Token);

        MessageReceived?.Invoke(new InboxMessage
        {
            Kind = MessageKind.File,
            From = string.IsNullOrWhiteSpace(header.Name) ? remote : header.Name!,
            FromAddress = remote,
            Title = header.Title ?? "",
            ReceivedAt = DateTime.Now,
            FileName = fileName,
            FilePath = finalPath,
            FileSize = size
        });
    }

    /// <summary>
    /// Reduces a name supplied by another machine to something safe to create
    /// in the downloads folder. The remote side is not trusted: a name like
    /// <c>..\..\Windows\System32\evil.dll</c> must not escape the folder, so
    /// every directory component is discarded and the remainder is scrubbed.
    /// </summary>
    private static string SafeFileName(string? requested)
    {
        var name = requested ?? "";

        // Cut anything that looks like a path, in either separator style, before
        // asking the framework for the leaf.
        var cut = name.LastIndexOfAny(['/', '\\', ':']);
        if (cut >= 0) name = name[(cut + 1)..];
        name = Path.GetFileName(name);

        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        name = name.Trim().TrimEnd('.');

        if (name.Length == 0 || name is "." or "..")
            name = "received-file";

        // Windows refuses these regardless of extension.
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
                             "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
                             "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase))
            name = "_" + name;

        // Leave room for the collision suffix and the .part extension.
        if (name.Length > 200) name = name[..200];

        return name;
    }

    /// <summary>Appends " (2)", " (3)" and so on rather than overwriting an existing file.</summary>
    private static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path) && !File.Exists(path + ".part")) return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; index < int.MaxValue; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".part")) return candidate;
        }

        throw new IOException($"Could not find a free name for {fileName}.");
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
