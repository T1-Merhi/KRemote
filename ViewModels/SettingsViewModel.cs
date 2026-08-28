using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KRemote.Models;
using KRemote.Platform;
using KRemote.Services;

namespace KRemote.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly SessionService _session;
    private readonly INotifier _notifier;
    private readonly IFolderPicker _folders;

    [ObservableProperty]
    private string status = "";

    public SettingsViewModel(
        SettingsService settings,
        SessionService session,
        INotifier notifier,
        IFolderPicker folders)
    {
        _settings = settings;
        _session = session;
        _notifier = notifier;
        _folders = folders;
    }

    private AppSettings Current => _settings.Current;

    public bool SupportsFolderChoice => _settings.Paths.SupportsFolderChoice;

    public bool SupportsSound => _notifier.SupportsSound;

    public bool SupportsWindowFlash => _notifier.SupportsWindowFlash;

    public string SettingsLocation => _settings.Location;

    public string MessagesLocation => _session.StoreLocation;

    public string DisplayName
    {
        get => Current.DisplayName;
        set
        {
            if (Current.DisplayName == value) return;
            Current.DisplayName = value.Trim();
            OnPropertyChanged();
            Persist();
        }
    }

    public string DownloadsFolder
    {
        get => _settings.EffectiveDownloadsFolder;
        set
        {
            if (Current.DownloadsFolder == value) return;
            Current.DownloadsFolder = value;
            OnPropertyChanged();
            Persist();
        }
    }

    public bool UseZipMode
    {
        get => Current.MultiFileMode == MultiFileSendMode.Zip;
        set
        {
            var mode = value ? MultiFileSendMode.Zip : MultiFileSendMode.Grouped;
            if (Current.MultiFileMode == mode) return;
            Current.MultiFileMode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseGroupedMode));
            Persist();
        }
    }

    public bool UseGroupedMode
    {
        get => Current.MultiFileMode == MultiFileSendMode.Grouped;
        set => UseZipMode = !value;
    }

    public string GroupTimeoutSeconds
    {
        get => Current.GroupTimeoutSeconds.ToString();
        set
        {
            if (!int.TryParse(value, out var seconds) || seconds <= 0)
            {
                GroupTimeoutValid = false;
                OnPropertyChanged(nameof(GroupTimeoutValid));
                return;
            }

            GroupTimeoutValid = true;
            Current.GroupTimeoutSeconds = seconds;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupTimeoutValid));
            Persist();
        }
    }

    public bool GroupTimeoutValid { get; private set; } = true;

    public bool NotifyToast
    {
        get => Current.NotifyToast;
        set { Current.NotifyToast = value; OnPropertyChanged(); Persist(); }
    }

    public bool NotifySound
    {
        get => Current.NotifySound;
        set { Current.NotifySound = value; OnPropertyChanged(); Persist(); }
    }

    public bool NotifyTaskbarFlash
    {
        get => Current.NotifyTaskbarFlash;
        set { Current.NotifyTaskbarFlash = value; OnPropertyChanged(); Persist(); }
    }

    public bool NotifyUnreadBadge
    {
        get => Current.NotifyUnreadBadge;
        set { Current.NotifyUnreadBadge = value; OnPropertyChanged(); Persist(); _session.NotifyChanged(); }
    }

    public bool PinEnabled
    {
        get => Current.PinEnabled;
        set
        {
            Current.PinEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinHint));
            OnPropertyChanged(nameof(PinValid));
            Persist();
        }
    }

    public string Pin
    {
        get => Current.Pin;
        set
        {
            Current.Pin = value.Trim();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinHint));
            OnPropertyChanged(nameof(PinValid));
            Persist();
        }
    }

    public bool PinValid => !Current.PinEnabled || (Current.Pin.Length == 4 && Current.Pin.All(char.IsDigit));

    public string PinHint => PinValid
        ? "Four digits."
        : "Enter 4 digits, or nobody will be able to send to this device.";

    [RelayCommand]
    private async Task BrowseDownloadsAsync()
    {
        var picked = await _folders.PickAsync(DownloadsFolder);
        if (string.IsNullOrWhiteSpace(picked)) return;

        DownloadsFolder = picked;
    }

    [RelayCommand]
    private void ResetDownloads()
    {
        Current.DownloadsFolder = "";
        OnPropertyChanged(nameof(DownloadsFolder));
        Persist();
    }

    [RelayCommand]
    private async Task CopyPinAsync()
    {
        try
        {
            await Clipboard.Default.SetTextAsync(Current.Pin);
            Status = "PIN copied to clipboard.";
        }
        catch (Exception e)
        {
            Status = $"Copy failed: {e.Message}";
        }
    }

    private void Persist()
    {
        try { _settings.Save(); }
        catch (Exception e) { Status = $"Could not save settings: {e.Message}"; }
    }
}
