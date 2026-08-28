using System.IO;
using System.Text.Json;
using KRemote.Models;
using KRemote.Platform;

namespace KRemote.Storage;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly IStoragePaths _paths;

    public SettingsStore(IStoragePaths paths)
    {
        _paths = paths;
    }

    public string Location => Path.Combine(_paths.AppDataDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Location)) return new AppSettings();
            var json = File.ReadAllText(Location);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_paths.AppDataDirectory);
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(Location, json);
    }
}
