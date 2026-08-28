using System.IO;
using System.Text.Json;
using KRemote.Models;

namespace KRemote.Storage;

public sealed class SettingsStore
{
    private static readonly string Directory_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRemote");

    private static readonly string FilePath = Path.Combine(Directory_, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Location => FilePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Directory_);
        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(FilePath, json);
    }
}
