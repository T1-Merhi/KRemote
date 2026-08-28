using CommunityToolkit.Mvvm.Input;
using KRemote.Models;
using KRemote.Platform;
using KRemote.Services;

namespace KRemote.ViewModels;

public sealed partial class SavedViewModel : MessageListViewModel
{
    public SavedViewModel(SessionService messages, IFileActions files)
        : base(messages, files)
    {
        Refresh();
    }

    protected override bool Accepts(SessionMessage message) => message.IsSaved;

    protected override string DescribeCounts()
    {
        var saved = Session.Messages.Count(m => m.IsSaved);
        return saved == 0
            ? "Nothing saved yet."
            : $"{saved} saved message{(saved == 1 ? "" : "s")}.";
    }

    [RelayCommand]
    private void Unsave()
    {
        if (Selected is not { } message) return;

        Session.SetSaved(message, false);
        Selected = null;
    }
}
