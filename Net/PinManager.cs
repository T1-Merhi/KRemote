using KRemote.Models;

namespace KRemote.Net;

/// <summary>
/// PIN protection state for this app run. "Unlocking" a peer here is a UX
/// convenience for the Share popup only -- it decides whether to show the
/// inline PIN prompt again, and caches the entered PIN so it can be
/// auto-attached to subsequent sends this session. The real security boundary
/// is the receiver's own unconditional check in <see cref="PeerServer"/>,
/// which runs on every text/file frame regardless of any unlock state here.
/// </summary>
public sealed class PinManager
{
    private readonly AppSettings _settings;

    /// <summary>Generated once per run when the mode is RandomEachLaunch; unused otherwise.</summary>
    public string SessionPin { get; }

    public PinManager(AppSettings settings)
    {
        _settings = settings;
        SessionPin = settings.PinMode == PinMode.RandomEachLaunch
            ? Random.Shared.Next(0, 10000).ToString("D4")
            : "";
    }

    public bool Enabled => _settings.PinEnabled;

    /// <summary>The PIN a sender must currently supply, resolved for whichever mode is active.</summary>
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

    /// <summary>Called when the Share popup rescans, per the "unlocked until you rescan" rule.</summary>
    public void ResetSession()
    {
        _unlockedAddresses.Clear();
        _cachedPinPerAddress.Clear();
    }
}
