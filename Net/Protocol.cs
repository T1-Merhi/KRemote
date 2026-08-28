using System.Text.Json;
using System.Text.Json.Serialization;

namespace KRemote.Net;

public static class Protocol
{
    public const int Port = 5555;

    public const int ChunkSize = 64 * 1024;

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
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("protected")] public bool? Protected { get; set; }
    [JsonPropertyName("pin")] public string? Pin { get; set; }

    public static Frame Ping() => new() { Type = "ping" };

    public static Frame Pong(string name, string? displayName = null, bool @protected = false) =>
        new() { Type = "pong", Name = name, DisplayName = Blank(displayName), Protected = @protected ? true : null };

    public static Frame Ok() => new() { Type = "ok" };
    public static Frame Ready() => new() { Type = "ready" };
    public static Frame Refused(string reason) => new() { Type = "refused", Error = reason };
    public static Frame VerifyPin(string pin) => new() { Type = "verifypin", Pin = pin };

    public static Frame TextMessage(string name, string? displayName, string? title, string text, string? pin = null) =>
        new() { Type = "text", Name = name, DisplayName = Blank(displayName), Title = Blank(title), Text = text, Pin = pin };

    public static Frame FileHeader(
        string name, string? displayName, string? title, string fileName, long size,
        string? text = null, string? groupId = null, int? groupCount = null, int? groupIndex = null, string? pin = null) =>
        new()
        {
            Type = "file", Name = name, DisplayName = Blank(displayName), Title = Blank(title),
            FileName = fileName, Size = size,
            Text = Blank(text), GroupId = groupId, GroupCount = groupCount, GroupIndex = groupIndex, Pin = pin
        };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
