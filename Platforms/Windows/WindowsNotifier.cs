using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Notifications;
using KRemote.Models;
using KRemote.Platform;

namespace KRemote.Platforms.Windows;

public sealed class WindowsNotifier : INotifier
{
    private readonly Func<AppSettings> _settings;

    public WindowsNotifier(Func<AppSettings> settings)
    {
        _settings = settings;
    }

    public bool SupportsSound => true;

    public bool SupportsWindowFlash => true;

    public void NotifyMessageReceived(SessionMessage message)
    {
        var settings = _settings();

        if (settings.NotifyToast) ShowToast(message);
        if (settings.NotifySound) MessageBeep(MB_ICONASTERISK);
        if (settings.NotifyTaskbarFlash) TaskbarFlash.Flash(CurrentWindowHandle());
    }

    private const uint MB_ICONASTERISK = 0x00000040;

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);

    private static void ShowToast(SessionMessage message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText($"From {message.From}")
                .AddText(message.Header)
                .Show();
        }
        catch (Exception)
        {
        }
    }

    private static IntPtr CurrentWindowHandle()
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return IntPtr.Zero;

        return WinRT.Interop.WindowNative.GetWindowHandle(native);
    }
}
