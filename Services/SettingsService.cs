using KRemote.Models;
using KRemote.Platform;
using KRemote.Storage;

namespace KRemote.Services;

public sealed class SettingsService
{
    private readonly SettingsStore _store;

    public SettingsService(SettingsStore store, IStoragePaths paths)
    {
        _store = store;
        Paths = paths;
        Current = store.Load();
    }

    public AppSettings Current { get; }

    public IStoragePaths Paths { get; }

    public string Location => _store.Location;

    public string EffectiveDownloadsFolder =>
        string.IsNullOrWhiteSpace(Current.DownloadsFolder)
            ? Paths.DefaultReceivedFilesDirectory
            : Current.DownloadsFolder;

    public event Action? Changed;

    public void Save()
    {
        _store.Save(Current);
        Changed?.Invoke();
    }
}
