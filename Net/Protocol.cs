using System.Text.Json;
using System.Text.Json.Serialization;

namespace KRemote.Net;

/// <summary>
/// Wire format: one UTF-8 JSON object per line over TCP, one request per
/// connection. Two verbs exist.
///
///   scan  ->  {"type":"ping"}                              (client)
///         &lt;-  {"type":"pong","name":"DESKTOP-A1"}         (server)
///
///   send  ->  {"type":"text","name":"DESKTOP-A1","text":"…"}
///         &lt;-  {"type":"ok"}
///
/// A line-delimited format is used so both sides can read exactly one frame
/// without needing a length prefix, and so the payload survives arbitrary
/// user text (JSON escapes the newlines inside "text").
/// </summary>
public static class Protocol
{
    /// <summary>TCP port every KRemote instance listens on and probes.</summary>
    public const int Port = 5555;

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
    [JsonPropertyName("text")] public string? Text { get; set; }

    public static Frame Ping() => new() { Type = "ping" };
    public static Frame Pong(string name) => new() { Type = "pong", Name = name };
    public static Frame Ok() => new() { Type = "ok" };
    public static Frame TextMessage(string name, string text) =>
        new() { Type = "text", Name = name, Text = text };
}
