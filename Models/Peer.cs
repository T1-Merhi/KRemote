namespace KRemote.Models;

/// <summary>
/// A KRemote instance that answered a probe during a network scan.
/// Peers are identified by the machine name their app reported, which is the
/// only label shown in the device list; the address is kept because it is what
/// we actually connect to when sending.
/// </summary>
public sealed class Peer
{
    public required string MachineName { get; init; }
    public required string Address { get; init; }

    /// <summary>Optional label the other PC chose for itself, shown alongside its machine name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Whether this peer requires a PIN before it accepts anything.</summary>
    public bool IsProtected { get; init; }

    /// <summary>What to show in the device list: the display name alongside the machine name, or just the machine name.</summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? MachineName : $"{DisplayName} ({MachineName})";

    public override string ToString() => MachineName;
}
