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

public sealed class AppSettings
{
    public string DisplayName { get; set; } = "";

    public string DownloadsFolder { get; set; } = "";

    public MultiFileSendMode MultiFileMode { get; set; } = MultiFileSendMode.Zip;

    public int GroupTimeoutSeconds { get; set; } = 60;

    public bool NotifyToast { get; set; } = true;
    public bool NotifySound { get; set; } = true;
    public bool NotifyTaskbarFlash { get; set; } = true;
    public bool NotifyUnreadBadge { get; set; } = true;

    public bool PinEnabled { get; set; }
    public PinMode PinMode { get; set; } = PinMode.Permanent;

    public string Pin { get; set; } = "";

    public bool FirstRunPromptShown { get; set; }
}
