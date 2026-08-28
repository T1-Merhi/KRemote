namespace KRemote.Models;

public enum MultiFileSendMode
{
    Zip,
    Grouped
}

public enum PinMode
{
    Permanent,
    RandomEachLaunch
}

/// <summary>
/// This PC's persisted preferences: identity, notifications, multi-file
/// sending, and PIN protection. Unlike <see cref="InboxMessage"/> this is not
/// bindable -- the Settings tab reads and writes fields directly and saves
/// immediately on every change, rather than through two-way binding.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Optional label shown to other PCs alongside this machine's Windows name.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Override for where received files land. Empty means use the built-in default.</summary>
    public string DownloadsFolder { get; set; } = "";

    public MultiFileSendMode MultiFileMode { get; set; } = MultiFileSendMode.Zip;

    /// <summary>Seconds an incomplete grouped send waits before its partial attachments are surfaced.</summary>
    public int GroupTimeoutSeconds { get; set; } = 60;

    public bool NotifyToast { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public bool NotifyTaskbarFlash { get; set; } = true;
    public bool NotifyUnreadBadge { get; set; } = true;

    public bool PinEnabled { get; set; }
    public PinMode PinMode { get; set; } = PinMode.Permanent;

    /// <summary>4-digit PIN. Only meaningful when <see cref="PinEnabled"/> and <see cref="PinMode"/> is Permanent.</summary>
    public string Pin { get; set; } = "";

    /// <summary>Whether the one-time "enable PIN protection?" prompt has already been shown.</summary>
    public bool FirstRunPromptShown { get; set; }
}
