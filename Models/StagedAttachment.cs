namespace KRemote.Models;

/// <summary>
/// One file the user has attached in the Share popup but not yet sent. UI-only
/// and never serialized -- chips are added and removed from an
/// ObservableCollection, never mutated in place.
/// </summary>
public sealed class StagedAttachment
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required long Size { get; init; }

    public string SizeText => InboxMessage.FormatSize(Size);
}
