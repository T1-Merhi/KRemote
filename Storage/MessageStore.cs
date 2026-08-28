using System.IO;
using System.Text.Json;
using KRemote.Models;

namespace KRemote.Storage;

public sealed class MessageStore
{
    private static readonly string Directory_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRemote");

    private static readonly string FilePath = Path.Combine(Directory_, "saved-messages.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Location => FilePath;

    public List<InboxMessage> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
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
        Directory.CreateDirectory(Directory_);
        var json = JsonSerializer.Serialize(saved.ToList(), Options);
        File.WriteAllText(FilePath, json);
    }
}
