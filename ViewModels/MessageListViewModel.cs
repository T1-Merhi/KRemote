using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KRemote.Models;
using KRemote.Platform;
using KRemote.Services;

namespace KRemote.ViewModels;

public abstract partial class MessageListViewModel : ObservableObject
{
    protected readonly InboxService Inbox;
    protected readonly IFileActions Files;

    [ObservableProperty]
    private InboxMessage? selected;

    [ObservableProperty]
    private string status = "";

    [ObservableProperty]
    private string detail = "";

    [ObservableProperty]
    private bool isWide = true;

    protected MessageListViewModel(InboxService inbox, IFileActions files)
    {
        Inbox = inbox;
        Files = files;

        Inbox.Changed += Refresh;
        Inbox.Messages.CollectionChanged += (_, _) => Refresh();
    }

    public ObservableCollection<InboxMessage> Items { get; } = [];

    public bool SupportsReveal => Files.SupportsReveal;

    public bool SupportsShare => Files.SupportsShare;

    public bool HasSelection => Selected is not null;

    public bool SelectionIsFile => Selected is { IsFile: true };

    public bool IsEmpty => Items.Count == 0;

    public bool HasNoSelection => Selected is null;

    public bool ShowList => IsWide || Selected is null;

    public bool ShowDetail => IsWide || Selected is not null;

    public bool ShowBack => !IsWide && Selected is not null;

    partial void OnIsWideChanged(bool value) => RaiseLayoutChanged();

    private void RaiseLayoutChanged()
    {
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowDetail));
        OnPropertyChanged(nameof(ShowBack));
    }

    [RelayCommand]
    private void Back() => Selected = null;

    protected abstract bool Accepts(InboxMessage message);

    protected abstract string DescribeCounts();

    public virtual void Refresh()
    {
        var wanted = Inbox.Messages.Where(Accepts).ToList();

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Items[i])) Items.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var index = Items.IndexOf(wanted[i]);
            if (index < 0) Items.Insert(i, wanted[i]);
            else if (index != i) Items.Move(index, i);
        }

        if (Selected is not null && !Items.Contains(Selected)) Selected = null;

        Status = DescribeCounts();
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnSelectedChanged(InboxMessage? value)
    {
        Detail = value is null ? "" : Describe(value);

        if (value is { IsUnread: true })
        {
            value.IsUnread = false;
            Inbox.NotifyChanged();
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(SelectionIsFile));
        RaiseLayoutChanged();
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (Selected is not { IsFile: true } message) return;

        try { await Files.OpenAsync(message.FilePath); }
        catch (Exception e) { Status = $"Could not open the file: {e.Message}"; }
    }

    [RelayCommand]
    private async Task RevealAsync()
    {
        if (Selected is not { IsFile: true } message) return;

        try { await Files.RevealAsync(message.FilePath); }
        catch (Exception e) { Status = $"Could not open the folder: {e.Message}"; }
    }

    [RelayCommand]
    private async Task ShareAsync()
    {
        if (Selected is not { IsFile: true } message) return;

        try { await Files.ShareAsync(message.FilePath); }
        catch (Exception e) { Status = $"Could not share the file: {e.Message}"; }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (Selected is not { } message) return;

        try
        {
            await Clipboard.Default.SetTextAsync(message.IsFile ? message.FilePath : message.Text);
            Status = message.IsFile ? "File path copied to clipboard." : "Copied to clipboard.";
        }
        catch (Exception e)
        {
            Status = $"Copy failed: {e.Message}";
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { } message) return;

        var wasFile = message.IsFile;
        Inbox.Remove(message);
        Selected = null;

        if (wasFile) Status = "Removed from the inbox. The file itself is still on disk.";
    }

    protected static string Describe(InboxMessage message)
    {
        if (!message.IsFile) return message.Text;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Title)) lines.Add(message.Title.Trim());
        if (!string.IsNullOrWhiteSpace(message.Text)) { lines.Add(message.Text.Trim()); lines.Add(""); }

        if (message.IsGroup)
        {
            lines.Add($"{message.Attachments.Count} files:");
            foreach (var attachment in message.Attachments)
                lines.Add($"  {attachment.FileName}  ({InboxMessage.FormatSize(attachment.FileSize)})");
        }
        else
        {
            lines.Add(message.FileName);
            lines.Add($"{InboxMessage.FormatSize(message.FileSize)}  ({message.FileSize:N0} bytes)");
        }

        lines.Add($"From {message.From} ({message.FromAddress}) at {message.ReceivedAt:MMM d, yyyy HH:mm:ss}");

        if (!message.IsGroup)
        {
            lines.Add("");
            lines.Add("Saved to:");
            lines.Add(message.FilePath);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
