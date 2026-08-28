using KRemote.Models;

namespace KRemote.Net;

public sealed class PinManager
{
    private readonly AppSettings _settings;
    private string? _sessionPin;

    public PinManager(AppSettings settings)
    {
        _settings = settings;
    }

    public string SessionPin => _sessionPin ??= Random.Shared.Next(0, 10000).ToString("D4");

    public string CurrentPin => _settings.PinMode == PinMode.RandomEachLaunch ? SessionPin : _settings.Pin;

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
