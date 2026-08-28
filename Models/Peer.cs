namespace KRemote.Models;

public sealed class Peer
{
    public required string MachineName { get; init; }
    public required string Address { get; init; }

    public string? DisplayName { get; init; }

    public bool IsProtected { get; init; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? MachineName : $"{DisplayName} ({MachineName})";

    public override string ToString() => MachineName;
}
