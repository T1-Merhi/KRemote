using KRemote.Models;

namespace KRemote.Net;

public sealed class PinManager
{
    private readonly AppSettings _settings;

    public PinManager(AppSettings settings)
    {
        _settings = settings;
    }

    public string CurrentPin => _settings.Pin;

    private readonly HashSet<string> _unlockedAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cachedPinPerAddress = new(StringComparer.OrdinalIgnoreCase);

    public bool IsUnlocked(string address) => _unlockedAddresses.Contains(address);

    public void Unlock(string address, string enteredPin)
    {
        _unlockedAddresses.Add(address);
        _cachedPinPerAddress[address] = enteredPin;
    }

    public string? GetCachedPin(string address) =>
        _cachedPinPerAddress.TryGetValue(address, out var pin) ? pin : null;

    public void ResetSession()
    {
        _unlockedAddresses.Clear();
        _cachedPinPerAddress.Clear();
    }
}
