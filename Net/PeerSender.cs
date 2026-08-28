using System.IO;
using System.Net.Sockets;
using KRemote.Models;

namespace KRemote.Net;

/// <summary>
/// The sending half: opens a connection to one chosen peer, hands over a text
/// message or a file, and waits for the acknowledgement. Any failure is thrown
/// so the UI can say the delivery did not happen rather than silently
/// pretending it did.
/// </summary>
public static class PeerSender
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public static async Task SendTextAsync(string address, string? title, string text, CancellationToken ct)
    {
        using var client = await ConnectAsync(address, ct);
        using var stream = client.GetStream();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Protocol.StallTimeout);

        var frame = Frame.TextMessage(Environment.MachineName, title, text);
        await LineIO.WriteLineAsync(stream, Protocol.Serialize(frame), timeout.Token);

        await ExpectAsync(stream, "ok", timeout);
    }

    /// <summary>
    /// Streams one file to a peer, reporting bytes sent as it goes. The file is
    /// read and written in <see cref="Protocol.ChunkSize"/> pieces, so its size
    /// is bounded by the disk rather than by memory.
    /// </summary>
    public static Task SendFileAsync(
        string address, string? title, string filePath, IProgress<long>? progress, CancellationToken ct) =>
        SendFileAsync(address, title, null, filePath, progress, ct, null, null, null);

    private static async Task SendFileAsync(
        string address, string? title, string? text, string filePath, IProgress<long>? progress, CancellationToken ct,
        string? groupId, int? groupCount, int? groupIndex)
    {
        await using var file = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            Protocol.ChunkSize, useAsync: true);

        var size = file.Length;
        var fileName = Path.GetFileName(filePath);

        using var client = await ConnectAsync(address, ct);
        using var stream = client.GetStream();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Protocol.StallTimeout);

        var header = Frame.FileHeader(Environment.MachineName, title, fileName, size, text, groupId, groupCount, groupIndex);
        await LineIO.WriteLineAsync(stream, Protocol.Serialize(header), timeout.Token);

        // The receiver vets the name and the folder before any bytes move.
        await ExpectAsync(stream, "ready", timeout);

        var buffer = new byte[Protocol.ChunkSize];
        var sent = 0L;
        progress?.Report(0);

        while (sent < size)
        {
            timeout.CancelAfter(Protocol.StallTimeout);

            var read = await file.ReadAsync(buffer, timeout.Token);
            if (read == 0)
                throw new IOException($"{fileName} ended after {sent:N0} of {size:N0} bytes -- was it modified mid-send?");

            await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            sent += read;
            progress?.Report(sent);
        }

        await stream.FlushAsync(timeout.Token);
        await ExpectAsync(stream, "ok", timeout);
    }

    /// <summary>
    /// Sends one or more files as a single logical message. A single file is
    /// sent exactly as <see cref="SendFileAsync(string,string?,string,IProgress{long}?,CancellationToken)"/>
    /// already does. Several files go out either zipped into one archive or as
    /// a grouped sequence of individually saved files, per <paramref name="mode"/>.
    /// </summary>
    public static async Task SendFilesAsync(
        string address, string? title, string? text, IReadOnlyList<string> filePaths, MultiFileSendMode mode,
        IProgress<(int fileIndex, int fileCount, long sent, long total)>? progress, CancellationToken ct)
    {
        if (filePaths.Count == 1)
        {
            var single = new Progress<long>(sent => progress?.Report((0, 1, sent, new FileInfo(filePaths[0]).Length)));
            await SendFileAsync(address, title, text, filePaths[0], single, ct, null, null, null);
            return;
        }

        if (mode == MultiFileSendMode.Zip)
        {
            var zipPath = Zip.CreateTempArchive(filePaths, title);
            try
            {
                var size = new FileInfo(zipPath).Length;
                var zipProgress = new Progress<long>(sent => progress?.Report((0, 1, sent, size)));
                await SendFileAsync(address, title, text, zipPath, zipProgress, ct, null, null, null);
            }
            finally
            {
                try { File.Delete(zipPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return;
        }

        // Grouped mode: one connection per file, tagged with a shared group id.
        var groupId = Guid.NewGuid().ToString("N");
        for (var i = 0; i < filePaths.Count; i++)
        {
            var index = i;
            var fileProgress = new Progress<long>(sent =>
                progress?.Report((index, filePaths.Count, sent, new FileInfo(filePaths[index]).Length)));

            await SendFileAsync(
                address, index == 0 ? title : null, index == 0 ? text : null, filePaths[index],
                fileProgress, ct, groupId, filePaths.Count, index);
        }
    }

    private static async Task<TcpClient> ConnectAsync(string address, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(address, Protocol.Port, connectCts.Token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads one reply and insists it is the expected verb. A "refused" frame
    /// carries the receiver's reason, which is far more useful to show than a
    /// generic failure.
    /// </summary>
    private static async Task ExpectAsync(NetworkStream stream, string type, CancellationTokenSource timeout)
    {
        timeout.CancelAfter(Protocol.StallTimeout);

        var line = await LineIO.ReadLineAsync(stream, timeout.Token);
        var reply = Protocol.Deserialize(line ?? "");

        if (reply?.Type == type) return;

        if (reply?.Type == "refused")
            throw new IOException(reply.Error ?? "The other PC refused the transfer.");

        throw new IOException($"Expected \"{type}\" from the other PC but got \"{reply?.Type ?? "nothing"}\".");
    }
}
