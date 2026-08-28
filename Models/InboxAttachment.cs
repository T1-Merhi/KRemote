namespace KRemote.Models;

public sealed class InboxAttachment
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
}
