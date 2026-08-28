using KRemote.Net;
using KRemote.Platform;
using KRemote.Services;
using KRemote.Storage;
using KRemote.ViewModels;
using KRemote.Views;
using Microsoft.Extensions.Logging;

namespace KRemote;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IStoragePaths, StoragePaths>();
        builder.Services.AddSingleton<IDeviceIdentity, DeviceIdentity>();
        builder.Services.AddSingleton<IFileActions, FileActions>();
        builder.Services.AddSingleton<IFolderPicker, FolderPicker>();

#if WINDOWS
        builder.Services.AddSingleton<INotifier>(sp =>
            new Platforms.Windows.WindowsNotifier(() => sp.GetRequiredService<SettingsService>().Current));
#elif ANDROID
        builder.Services.AddSingleton<INotifier>(sp =>
            new Platforms.Android.AndroidNotifier(() => sp.GetRequiredService<SettingsService>().Current));
#endif

        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton<MessageStore>();
        builder.Services.AddSingleton<SettingsService>();

        builder.Services.AddSingleton(sp =>
            new PinManager(sp.GetRequiredService<SettingsService>().Current));

        builder.Services.AddSingleton<PeerSender>();
        builder.Services.AddSingleton<SessionService>();

        builder.Services.AddSingleton<ActiveSessionViewModel>();
        builder.Services.AddSingleton<SavedViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddTransient<ShareViewModel>();

        builder.Services.AddSingleton<ActiveSessionPage>();
        builder.Services.AddSingleton<SavedPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddTransient<SharePage>();
        builder.Services.AddTransient<FirstRunPinPage>();

        return builder.Build();
    }
}
