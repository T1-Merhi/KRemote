using KRemote.Models;

namespace KRemote.Platform;

public interface INotifier
{
    bool SupportsSound { get; }

    bool SupportsWindowFlash { get; }

    void NotifyMessageReceived(SessionMessage message);
}
