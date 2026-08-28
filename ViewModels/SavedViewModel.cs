using CommunityToolkit.Mvvm.Input;
using KRemote.Models;
using KRemote.Platform;
using KRemote.Services;

namespace KRemote.ViewModels;

public sealed partial class SavedViewModel : MessageListViewModel
{
    public SavedViewModel(InboxService inbox, IFileActions files)
        : base(inbox, files)
    {
        Refresh();
    }

    protected override bool Accepts(InboxMessage message) => message.IsSaved;

    protected override string DescribeCounts()
    {
        var saved = Inbox.Messages.Count(m => m.IsSaved);
        return saved == 0
            ? "Nothing saved yet."
            : $"{saved} saved message{(saved == 1 ? "" : "s")}.";
    }

    [RelayCommand]
    private void Unsave()
    {
        if (Selected is not { } message) return;

        Inbox.SetSaved(message, false);
        Selected = null;
    }
}
