using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KRemote.Models;
using KRemote.Platform;
using KRemote.Services;

namespace KRemote.ViewModels;

public sealed partial class ActiveSessionViewModel : MessageListViewModel
{
    private readonly SettingsService _settings;

    [ObservableProperty]
    private string transferStatus = "";

    [ObservableProperty]
    private bool isTransferring;

    public ActiveSessionViewModel(SessionService messages, IFileActions files, SettingsService settings)
        : base(messages, files)
    {
        _settings = settings;

        Session.TransferProgress += OnTransferProgress;
        Refresh();
    }

    public int UnreadCount => _settings.Current.NotifyUnreadBadge ? Session.UnreadCount : 0;

    public bool HasUnread => UnreadCount > 0;

    public string UnreadBadge => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public string ListenBanner => Session.ListenError ?? "";

    public bool HasListenError => Session.ListenError is not null;

    public bool CanSaveSelected => Selected is { IsSaved: false };

    protected override bool Accepts(SessionMessage message) => !message.IsRestored;

    protected override string DescribeCounts()
    {
        if (Items.Count == 0) return "Nothing received yet.";

        var saved = Items.Count(m => m.IsSaved);
        var files = Items.Count(m => m.IsFile);

        var parts = new List<string> { $"{Items.Count} in this session" };
        if (files > 0) parts.Add($"{files} file{(files == 1 ? "" : "s")}");
        if (saved > 0) parts.Add($"{saved} saved to disk");

        return string.Join("  ·  ", parts);
    }

    private void OnTransferProgress(string fileName, long received, long total)
    {
        if (received >= total)
        {
            IsTransferring = false;
            TransferStatus = "";
            Refresh();
            return;
        }

        IsTransferring = true;

        var percent = total > 0 ? received * 100.0 / total : 0;
        TransferStatus = $"Receiving {fileName}… {percent:0}%  " +
                         $"({SessionMessage.FormatSize(received)} of {SessionMessage.FormatSize(total)})";
    }

    [RelayCommand]
    private void Save()
    {
        if (Selected is not { IsSaved: false } message) return;

        Session.SetSaved(message, true);
        OnPropertyChanged(nameof(CanSaveSelected));
    }

    [RelayCommand]
    private async Task ComposeAsync() => await Shell.Current.GoToAsync("share");

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadBadge));
        OnPropertyChanged(nameof(CanSaveSelected));
        OnPropertyChanged(nameof(ListenBanner));
        OnPropertyChanged(nameof(HasListenError));
    }
}
