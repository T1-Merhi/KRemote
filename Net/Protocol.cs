using System.Text.Json;
using System.Text.Json.Serialization;

namespace KRemote.Net;

/// <summary>
/// Wire format: newline-delimited UTF-8 JSON headers over TCP, one exchange per
/// connection. Three verbs exist.
///
///   scan  ->  {"type":"ping"}                              (client)
///         &lt;-  {"type":"pong","name":"DESKTOP-A1"}         (server)
///
///   text  ->  {"type":"text","name":"DESKTOP-A1","text":"…"}
///         &lt;-  {"type":"ok"}
///
///   file  ->  {"type":"file","name":"DESKTOP-A1","fileName":"a.pdf","size":41234}
///         &lt;-  {"type":"ready"}
///         ->  &lt;exactly `size` raw bytes&gt;
///         &lt;-  {"type":"ok"}
///
/// Text rides inside the JSON, which escapes newlines and keeps the framing
/// intact. File bytes do not: they follow the header as an unescaped stream of
/// known length, so a multi-gigabyte file never has to exist in memory on
/// either side. The receiver answers "ready" before the first byte moves, which
/// is what lets it reject a transfer (bad name, unwritable folder) without the
/// sender having pushed the whole file first.
///
/// Sending several files at once takes one of two shapes, chosen by the
/// sender's Settings:
///
///   - Zip mode needs no protocol change at all: the files are zipped into one
///     archive first and sent as an ordinary single "file" frame.
///   - Grouped mode tags each file frame with a shared group id so the
///     receiver can stitch the results back into one inbox entry with several
///     attachments, while every exchange (ready/bytes/ok) stays exactly the
///     same as a normal single-file transfer -- grouping is purely a
///     receiver-side bookkeeping step layered on top:
///
///   group ->  {"type":"file","name":"A","fileName":"a.pdf","size":123,"groupId":"g1","groupCount":2,"groupIndex":0,"text":"see attached"}
///         &lt;-  {"type":"ready"} / &lt;bytes&gt; / {"type":"ok"}
///         ->  {"type":"file","name":"A","fileName":"b.pdf","size":456,"groupId":"g1","groupCount":2,"groupIndex":1}
///         &lt;-  {"type":"ready"} / &lt;bytes&gt; / {"type":"ok"}
///
/// A description typed alongside the files rides in Text on the first file of
/// the group (index 0) only, so the receiver attaches it once, not per file.
/// All group fields are optional and additive: an old receiver without them
/// simply gets each file as its own separate inbox entry, which is a safe,
/// non-corrupting degradation.
/// </summary>
public static class Protocol
{
    /// <summary>TCP port every KRemote instance listens on and probes.</summary>
    public const int Port = 5555;

    /// <summary>Chunk size for streaming file bodies in both directions.</summary>
    public const int ChunkSize = 64 * 1024;

    /// <summary>
    /// How long either side will wait for more bytes mid-transfer. This is a
    /// stall timeout, refreshed on every chunk, not a cap on the transfer --
    /// a large file over a slow link must not be killed for being slow.
    /// </summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(Frame frame) => JsonSerializer.Serialize(frame, Json);

    public static Frame? Deserialize(string line)
    {
        try { return JsonSerializer.Deserialize<Frame>(line, Json); }
        catch (JsonException) { return null; }
    }
}

public sealed class Frame
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("fileName")] public string? FileName { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("groupId")] public string? GroupId { get; set; }
    [JsonPropertyName("groupCount")] public int? GroupCount { get; set; }
    [JsonPropertyName("groupIndex")] public int? GroupIndex { get; set; }

    public static Frame Ping() => new() { Type = "ping" };
    public static Frame Pong(string name) => new() { Type = "pong", Name = name };
    public static Frame Ok() => new() { Type = "ok" };
    public static Frame Ready() => new() { Type = "ready" };
    public static Frame Refused(string reason) => new() { Type = "refused", Error = reason };

    // Title is optional on both message kinds; null keeps it off the wire.
    public static Frame TextMessage(string name, string? title, string text) =>
        new() { Type = "text", Name = name, Title = Blank(title), Text = text };

    public static Frame FileHeader(
        string name, string? title, string fileName, long size,
        string? text = null, string? groupId = null, int? groupCount = null, int? groupIndex = null) =>
        new()
        {
            Type = "file", Name = name, Title = Blank(title), FileName = fileName, Size = size,
            Text = Blank(text), GroupId = groupId, GroupCount = groupCount, GroupIndex = groupIndex
        };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
