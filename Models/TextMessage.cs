using System.ComponentModel;
using System.Text.Json.Serialization;

namespace KRemote.Models;

/// <summary>
/// One block of text that arrived from another PC.
///
/// The inbox lives in memory only: messages disappear when the app closes
/// unless the user presses Save, which writes them to the on-disk store and
/// flips <see cref="IsSaved"/>. Saved messages are the ones reloaded on the
/// next launch.
/// </summary>
public sealed class TextMessage : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.Now;
    public string Text { get; set; } = "";

    private bool _isSaved;
    public bool IsSaved
    {
        get => _isSaved;
        set
        {
            if (_isSaved == value) return;
            _isSaved = value;
            OnPropertyChanged(nameof(IsSaved));
            OnPropertyChanged(nameof(Header));
        }
    }

    /// <summary>Single line shown as the bold row title in the inbox list.</summary>
    [JsonIgnore]
    public string Header => IsSaved
        ? $"{From}  ·  {ReceivedAt:MMM d, HH:mm}  ·  saved"
        : $"{From}  ·  {ReceivedAt:MMM d, HH:mm}";

    /// <summary>Flattened first slice of the body, for the list row subtitle.</summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            var flat = Text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
            return flat.Length <= 90 ? flat : flat[..90] + "…";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
