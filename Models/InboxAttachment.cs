namespace KRemote.Models;

/// <summary>One file within a grouped multi-file message.</summary>
public sealed class InboxAttachment
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
}
