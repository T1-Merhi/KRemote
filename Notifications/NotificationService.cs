using System.Media;
using System.Windows;
using CommunityToolkit.WinUI.Notifications;
using KRemote.Models;

namespace KRemote.Notifications;

/// <summary>
/// Fires the three "external" arrival signals -- toast, sound, taskbar flash --
/// each gated by its own Settings toggle. The fourth signal (the unread badge)
/// is pure in-app UI state tracked by MainWindow itself, since it isn't
/// something to show outside the app.
/// </summary>
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
            // Unpackaged toast delivery can fail to register on some Windows
            // builds; a missed notification is not worth crashing over.
        }
    }
}
