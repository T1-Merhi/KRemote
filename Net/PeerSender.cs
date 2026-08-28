using System.IO;
using System.Net.Sockets;
using KRemote.Models;
using KRemote.Platform;

namespace KRemote.Net;

public sealed class PeerSender
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly IDeviceIdentity _identity;

    public PeerSender(IDeviceIdentity identity)
    {
        _identity = identity;
    }

    public async Task SendTextAsync(
        string address, string? displayName, string? title, string text, CancellationToken ct, string? pin = null)
    {
        using var client = await ConnectAsync(address, ct);
        using var stream = client.GetStream();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Protocol.StallTimeout);

        var frame = Frame.TextMessage(_identity.MachineName, displayName, title, text, pin);
        await LineIO.WriteLineAsync(stream, Protocol.Serialize(frame), timeout.Token);

        await ExpectAsync(stream, "ok", timeout);
    }

    public Task SendFileAsync(
        string address, string? displayName, string? title, string filePath,
        IProgress<long>? progress, CancellationToken ct, string? pin = null) =>
        SendFileAsync(address, displayName, title, null, filePath, progress, ct, null, null, null, pin);

    private async Task SendFileAsync(
        string address, string? displayName, string? title, string? text, string filePath,
        IProgress<long>? progress, CancellationToken ct,
        string? groupId, int? groupCount, int? groupIndex, string? pin)
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

        var header = Frame.FileHeader(
            _identity.MachineName, displayName, title, fileName, size,
            text, groupId, groupCount, groupIndex, pin);
        await LineIO.WriteLineAsync(stream, Protocol.Serialize(header), timeout.Token);

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

    public async Task SendFilesAsync(
        string address, string? displayName, string? title, string? text,
        IReadOnlyList<string> filePaths, MultiFileSendMode mode,
        IProgress<(int fileIndex, int fileCount, long sent, long total)>? progress, CancellationToken ct, string? pin = null)
    {
        if (filePaths.Count == 1)
        {
            var single = new Progress<long>(sent => progress?.Report((0, 1, sent, new FileInfo(filePaths[0]).Length)));
            await SendFileAsync(address, displayName, title, text, filePaths[0], single, ct, null, null, null, pin);
            return;
        }

        if (mode == MultiFileSendMode.Zip)
        {
            var zipPath = Zip.CreateTempArchive(filePaths, title);
            try
            {
                var size = new FileInfo(zipPath).Length;
                var zipProgress = new Progress<long>(sent => progress?.Report((0, 1, sent, size)));
                await SendFileAsync(address, displayName, title, text, zipPath, zipProgress, ct, null, null, null, pin);
            }
            finally
            {
                try { File.Delete(zipPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return;
        }

        var groupId = Guid.NewGuid().ToString("N");
        for (var i = 0; i < filePaths.Count; i++)
        {
            var index = i;
            var fileProgress = new Progress<long>(sent =>
                progress?.Report((index, filePaths.Count, sent, new FileInfo(filePaths[index]).Length)));

            await SendFileAsync(
                address, displayName, index == 0 ? title : null, index == 0 ? text : null, filePaths[index],
                fileProgress, ct, groupId, filePaths.Count, index, pin);
        }
    }

    public async Task VerifyPinAsync(string address, string pin, CancellationToken ct)
    {
        using var client = await ConnectAsync(address, ct);
        using var stream = client.GetStream();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Protocol.StallTimeout);

        await LineIO.WriteLineAsync(stream, Protocol.Serialize(Frame.VerifyPin(pin)), timeout.Token);
        await ExpectAsync(stream, "ok", timeout);
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
