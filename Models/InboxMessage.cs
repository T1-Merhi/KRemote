using System.ComponentModel;
using System.Text.Json.Serialization;

namespace KRemote.Models;

public enum MessageKind
{
    Text,
    File
}

/// <summary>
/// One item in the inbox: either a block of text or a file that has been
/// written to disk.
///
/// The inbox lives in memory only. A message survives a restart only if the
/// user presses Save, which writes it to the on-disk store and flips
/// <see cref="IsSaved"/>. For a file, saving persists the row -- the bytes are
/// already on disk in the downloads folder either way, and deleting the row
/// never deletes the file.
/// </summary>
public sealed class InboxMessage : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    /// <summary>Optional label the sender typed. Empty when they did not.</summary>
    public string Title { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageKind Kind { get; set; } = MessageKind.Text;

    /// <summary>Body of a text message. Empty for a file.</summary>
    public string Text { get; set; } = "";

    /// <summary>Name the file was saved under, which may differ from the sender's if it collided.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Where the received file landed on this PC.</summary>
    public string FilePath { get; set; } = "";

    public long FileSize { get; set; }

    /// <summary>
    /// Populated for a grouped multi-file send. The single-file fields above
    /// still mirror the first attachment for backward-compatible display.
    /// </summary>
    public List<InboxAttachment> Attachments { get; set; } = [];

    [JsonIgnore]
    public bool IsGroup => Attachments.Count > 1;

    private bool _isSaved;
    public bool IsSaved
    {
        get => _isSaved;
        set
        {
            if (_isSaved == value) return;
            _isSaved = value;
            OnPropertyChanged(nameof(IsSaved));
            OnPropertyChanged(nameof(Meta));
        }
    }

    /// <summary>True until this row is selected/clicked in the inbox. Session-only, never persisted.</summary>
    [JsonIgnore]
    public bool IsUnread { get; set; } = true;

    [JsonIgnore]
    public bool IsFile => Kind == MessageKind.File;

    /// <summary>
    /// Bold first line of the inbox row: the sender's title when they gave one,
    /// otherwise whatever best identifies the message on its own.
    /// </summary>
    [JsonIgnore]
    public string Header
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return IsGroup ? $"{Title.Trim()} ({Attachments.Count} files)" : Title.Trim();
            if (IsGroup) return $"{Attachments.Count} files";
            if (IsFile) return FileName;

            var firstLine = Flatten(Text);
            if (firstLine.Length == 0) return "(empty message)";
            return firstLine.Length <= 70 ? firstLine : firstLine[..70] + "…";
        }
    }

    /// <summary>Muted second line: who sent it, when, and how big.</summary>
    [JsonIgnore]
    public string Meta
    {
        get
        {
            var parts = new List<string> { From, ReceivedAt.ToString("MMM d, HH:mm") };
            if (IsGroup) parts.Add(FormatSize(Attachments.Sum(a => a.FileSize)));
            else if (IsFile) parts.Add(FormatSize(FileSize));
            if (IsSaved) parts.Add("saved");
            return string.Join("  ·  ", parts);
        }
    }

    [JsonIgnore]
    public string DetailHeader => IsGroup
        ? $"{Attachments.Count} files  ({FormatSize(Attachments.Sum(a => a.FileSize))})"
        : IsFile
            ? $"{FileName}  ({FormatSize(FileSize)})"
            : Header;

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    private static string Flatten(string value)
    {
        var flat = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
        return flat;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
