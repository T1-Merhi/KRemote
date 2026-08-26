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

    public override string ToString() => MachineName;
}
