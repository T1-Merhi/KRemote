using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using KRemote.Models;
using KRemote.Platform;

namespace KRemote.Platforms.Android;

public sealed class AndroidNotifier : INotifier
{
    private const string ChannelId = "kremote-inbox";
    private const string ChannelName = "Incoming messages";

    private readonly Func<AppSettings> _settings;
    private int _nextId = 1;
    private bool _channelReady;

    public AndroidNotifier(Func<AppSettings> settings)
    {
        _settings = settings;
    }

    public bool SupportsSound => false;

    public bool SupportsWindowFlash => false;

    public void NotifyMessageReceived(SessionMessage message)
    {
        if (!_settings().NotifyToast) return;

        try
        {
            EnsureChannel();

            var context = global::Android.App.Application.Context;

            var notification = new NotificationCompat.Builder(context, ChannelId)
                .SetContentTitle($"From {message.From}")
                .SetContentText(message.Header)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(message.Header))
                .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownloadDone)
                .SetAutoCancel(true)
                .Build();

            NotificationManagerCompat.From(context).Notify(_nextId++, notification);
        }
        catch (Exception)
        {
        }
    }

    private void EnsureChannel()
    {
        if (_channelReady) return;
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) { _channelReady = true; return; }

        var context = global::Android.App.Application.Context;
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null) return;

        var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Default);
        manager.CreateNotificationChannel(channel);
        _channelReady = true;
    }
}
