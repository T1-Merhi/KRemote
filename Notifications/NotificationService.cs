using System.Media;
using System.Windows;
using CommunityToolkit.WinUI.Notifications;
using KRemote.Models;

namespace KRemote.Notifications;

public sealed class NotificationService
{
    private readonly Func<AppSettings> _settings;
    private readonly Window _window;

    public NotificationService(Func<AppSettings> settings, Window window)
    {
        _settings = settings;
        _window = window;
    }

    public void NotifyMessageReceived(InboxMessage message)
    {
        var settings = _settings();

        if (settings.NotifyToast) ShowToast(message);
        if (settings.NotifySound) SystemSounds.Asterisk.Play();
        if (settings.NotifyTaskbarFlash) TaskbarFlash.Flash(_window);
    }

    private static void ShowToast(InboxMessage message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText($"From {message.From}")
                .AddText(message.Header)
                .Show();
        }
        catch
        {
        }
    }
}
