using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KRemote.Models;
using KRemote.Platform;
using KRemote.Services;

namespace KRemote.ViewModels;

public sealed partial class InboxViewModel : MessageListViewModel
{
    private readonly SettingsService _settings;

    [ObservableProperty]
    private string transferStatus = "";

    [ObservableProperty]
    private bool isTransferring;

    public InboxViewModel(InboxService inbox, IFileActions files, SettingsService settings)
        : base(inbox, files)
    {
        _settings = settings;

        Inbox.TransferProgress += OnTransferProgress;
        Refresh();
    }

    public int UnreadCount => _settings.Current.NotifyUnreadBadge ? Inbox.UnreadCount : 0;

    public bool HasUnread => UnreadCount > 0;

    public string UnreadBadge => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public string ListenBanner => Inbox.ListenError ?? "";

    public bool HasListenError => Inbox.ListenError is not null;

    public bool CanSaveSelected => Selected is { IsSaved: false };

    protected override bool Accepts(InboxMessage message) => true;

    protected override string DescribeCounts()
    {
        if (Inbox.Messages.Count == 0) return "Nothing received yet.";

        var saved = Inbox.Messages.Count(m => m.IsSaved);
        var files = Inbox.Messages.Count(m => m.IsFile);

        var parts = new List<string> { $"{Inbox.Messages.Count} in this session" };
        if (files > 0) parts.Add($"{files} file{(files == 1 ? "" : "s")}");
        parts.Add($"{saved} saved to disk");

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
                         $"({InboxMessage.FormatSize(received)} of {InboxMessage.FormatSize(total)})";
    }

    [RelayCommand]
    private void Save()
    {
        if (Selected is not { IsSaved: false } message) return;

        Inbox.SetSaved(message, true);
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
