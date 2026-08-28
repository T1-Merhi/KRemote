namespace KRemote.Models;

public sealed class StagedAttachment
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required long Size { get; init; }

    public string SizeText => InboxMessage.FormatSize(Size);
}
