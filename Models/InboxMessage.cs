using System.ComponentModel;
using System.Text.Json.Serialization;

namespace KRemote.Models;

public enum MessageKind
{
    Text,
    File
}

public sealed class InboxMessage : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    public string Title { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageKind Kind { get; set; } = MessageKind.Text;

    public string Text { get; set; } = "";

    public string FileName { get; set; } = "";

    public string FilePath { get; set; } = "";

    public long FileSize { get; set; }

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

    private bool _isUnread = true;

    [JsonIgnore]
    public bool IsUnread
    {
        get => _isUnread;
        set
        {
            if (_isUnread == value) return;
            _isUnread = value;
            OnPropertyChanged(nameof(IsUnread));
        }
    }

    [JsonIgnore]
    public bool IsFile => Kind == MessageKind.File;

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
