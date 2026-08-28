using System.IO;
using System.Text.Json;
using KRemote.Models;
using KRemote.Platform;

namespace KRemote.Storage;

public sealed class MessageStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IStoragePaths _paths;

    public MessageStore(IStoragePaths paths)
    {
        _paths = paths;
    }

    public string Location => Path.Combine(_paths.AppDataDirectory, "saved-messages.json");

    public List<InboxMessage> Load()
    {
        try
        {
            if (!File.Exists(Location)) return [];
            var json = File.ReadAllText(Location);
            var messages = JsonSerializer.Deserialize<List<InboxMessage>>(json, Options) ?? [];
            foreach (var message in messages) message.IsSaved = true;
            return messages;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<InboxMessage> saved)
    {
        Directory.CreateDirectory(_paths.AppDataDirectory);
        var json = JsonSerializer.Serialize(saved.ToList(), Options);
        File.WriteAllText(Location, json);
    }
}
